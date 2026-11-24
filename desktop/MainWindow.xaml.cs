using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace GeminiWalletWidget;

public partial class MainWindow : Window
{
    private WalletSettings _settings = new();
    private WalletState _state = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reloadDebounceTimer;
    private readonly DispatcherTimer _bubbleHideTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _autoRefreshTimer;
    private readonly DispatcherTimer _postTurnRefreshTimer;
    private readonly DispatcherTimer _scopeTimer;
    private readonly Random _rng = new();
    private DateTimeOffset _lastExpiredAutoRefresh = DateTimeOffset.MinValue;
    private readonly Dictionary<uint, bool> _antigravityPidCache = new();
    private bool _userHiddenToTray;
    private bool _hiddenByAppScope;

    private Forms.NotifyIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

    private bool _isPointerDown;
    private bool _isDragging;
    private System.Windows.Point _dragStartScreen;
    private double _winStartLeft;
    private double _winStartTop;
    private bool _isHoveringBubble;

    private int _clickIndex;
    private string _lastQuote = "";
    private int _lastKnownTurns = -1;
    private bool _lowQuotaAlertPlayed;
    private bool _reallyClosing;

    private readonly string _assetsDir;
    private readonly bool _launchedAutomatically;
    private MediaPlayer? _activePlayer;
    private EventWaitHandle? _wakeEvent;
    private EventWaitHandle? _quitEvent;
    private CancellationTokenSource? _ipcCts;

    public MainWindow() : this(false) { }

    public MainWindow(bool launchedAutomatically)
    {
        InitializeComponent();
        _launchedAutomatically = launchedAutomatically;
        _assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");

        _reloadDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _reloadDebounceTimer.Tick += (_, _) =>
        {
            _reloadDebounceTimer.Stop();
            ReloadFromDisk(announceChanges: true);
        };

        _bubbleHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5200) };
        _bubbleHideTimer.Tick += (_, _) =>
        {
            _bubbleHideTimer.Stop();
            if (!_isHoveringBubble) HideBubble();
        };

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _tickTimer.Tick += (_, _) => UpdateCornerBadgeAndLiveBubble();

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            if (_settings.AutoRefreshEnabled)
                await TriggerAgySyncAsync(force: false, showBubbleOnComplete: false);
        };

        _postTurnRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(18) };
        _postTurnRefreshTimer.Tick += async (_, _) =>
        {
            _postTurnRefreshTimer.Stop();
            if (_settings.AutoRefreshEnabled && _settings.RefreshAfterTurn)
                await TriggerAgySyncAsync(force: false, showBubbleOnComplete: false);
        };

        _scopeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _scopeTimer.Tick += (_, _) => EnforceAntigravityWindowScope();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadMascotBitmap();
        InitTrayIcon();
        ReloadFromDisk(announceChanges: false);
        ApplySettingsToWindow(initialPosition: true);
        StartFileWatcher();
        StartIpcListeners();
        _tickTimer.Start();
        _scopeTimer.Start();
        ConfigureAutoRefreshTimer();
        EnforceAntigravityWindowScope();

        // Friendly greeting bubble on launch (if currently visible)
        if (IsVisible) ShowBubble("quota");

        // Trigger a silent background sync shortly after launch so quota is always fresh!
        if (_settings.AutoRefreshEnabled)
        {
            await Task.Delay(1200);
            await TriggerAgySyncAsync(force: false, showBubbleOnComplete: false);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private bool IsAllowedForegroundWindow()
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded && _settingsWindow.IsVisible)
            return true;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;
        if (pid == (uint)Environment.ProcessId) return true;

        if (_antigravityPidCache.TryGetValue(pid, out bool cached))
            return cached;

        bool isAgy = false;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            var name = proc.ProcessName ?? "";
            isAgy = name.Contains("antigravity", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            isAgy = false;
        }

        if (_antigravityPidCache.Count > 128) _antigravityPidCache.Clear();
        _antigravityPidCache[pid] = isAgy;
        return isAgy;
    }

    private void EnforceAntigravityWindowScope()
    {
        if (_reallyClosing || _userHiddenToTray) return;

        if (!_settings.OnlyShowInAntigravity)
        {
            if (_hiddenByAppScope)
            {
                _hiddenByAppScope = false;
                if (!IsVisible) Show();
            }
            return;
        }

        // Never hide mid-drag
        if (_isDragging || _isPointerDown) return;

        bool allowed = IsAllowedForegroundWindow();
        if (allowed)
        {
            if (_hiddenByAppScope || !IsVisible)
            {
                _hiddenByAppScope = false;
                Show();
                Topmost = _settings.AlwaysOnTop;
            }
        }
        else
        {
            if (IsVisible)
            {
                _hiddenByAppScope = true;
                Hide();
            }
        }
    }

    private void ConfigureAutoRefreshTimer()
    {
        int mins = Math.Clamp(_settings.AutoRefreshMinutes, 1, 60);
        _autoRefreshTimer.Interval = TimeSpan.FromMinutes(mins);
        if (_settings.AutoRefreshEnabled)
        {
            if (!_autoRefreshTimer.IsEnabled) _autoRefreshTimer.Start();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    public async Task<(bool Ok, string Message)> TriggerAgySyncAsync(bool force, bool showBubbleOnComplete)
    {
        var result = await WalletStore.SyncWithAgyAsync(force);
        ReloadFromDisk(announceChanges: false);
        if (showBubbleOnComplete)
        {
            ShowBubble("quota");
        }
        return result;
    }

    private void StartIpcListeners()
    {
        try
        {
            _ipcCts = new CancellationTokenSource();
            var token = _ipcCts.Token;
            _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.WakeEventName);
            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.QuitEventName);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var handles = new WaitHandle[] { _wakeEvent, _quitEvent, token.WaitHandle };
                while (!token.IsCancellationRequested)
                {
                    int idx = WaitHandle.WaitAny(handles);
                    if (idx == 0)
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!IsVisible) Show();
                            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                            ReloadFromDisk(announceChanges: false);
                            ShowBubble("quota");
                        });
                    }
                    else if (idx == 1)
                    {
                        Dispatcher.BeginInvoke(ExitApplication);
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
            });
        }
        catch { }
    }

    private void LoadMascotBitmap()
    {
        var path = Path.Combine(_assetsDir, "mascot.png");
        if (!File.Exists(path)) return;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        MascotImage.Source = bmp;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void InitTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Gemini 额度猫娘挂件",
            Visible = true,
        };

        var logoPath = Path.Combine(_assetsDir, "logo.png");
        if (File.Exists(logoPath))
        {
            try
            {
                using var bmp = new Bitmap(logoPath);
                using var small = new Bitmap(bmp, new System.Drawing.Size(32, 32));
                IntPtr hIcon = small.GetHicon();
                _trayIcon.Icon = (Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
            }
            catch
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
        }
        else
        {
            _trayIcon.Icon = SystemIcons.Application;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("🔄 立即从 agy 同步真实额度", null, (_, _) => Dispatcher.Invoke(async () => await TriggerAgySyncAsync(force: true, showBubbleOnComplete: true)));
        menu.Items.Add("显示 / 隐藏猫娘", null, (_, _) => Dispatcher.Invoke(ToggleVisibility));
        menu.Items.Add("手动校准 Gemini 额度...", null, (_, _) => Dispatcher.Invoke(() => OpenSettings(0)));
        menu.Items.Add("外观与台词设置...", null, (_, _) => Dispatcher.Invoke(() => OpenSettings(1)));
        menu.Items.Add("重置到右下角", null, (_, _) => Dispatcher.Invoke(ResetWindowPosition));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出挂件", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisibility);
    }

    private void StartFileWatcher()
    {
        WalletStore.EnsureDir();
        _watcher = new FileSystemWatcher(WalletStore.RuntimeDir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onChanged = (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _reloadDebounceTimer.Stop();
                _reloadDebounceTimer.Start();
            });
        };
        _watcher.Changed += onChanged;
        _watcher.Created += onChanged;
        _watcher.Renamed += (s, e) => onChanged(s, e);
    }

    public void ReloadFromDisk(bool announceChanges)
    {
        var prevTurns = _lastKnownTurns;
        _settings = WalletStore.LoadSettings();
        _state = WalletStore.LoadState();
        ApplySettingsToWindow(initialPosition: false);
        UpdateCornerBadgeAndLiveBubble();

        var curTurns = _state.Stats.TotalTurns;
        if (announceChanges && prevTurns >= 0 && curTurns > prevTurns)
        {
            if (_settings.TurnDoneChime) PlaySound("turn-done.wav");
            ShowCustomToastBubble("反重力对话已完成 ✨", $"当前模型：{_state.ActiveModel ?? "Gemini"} · 今日 {WalletStore.GetTodayStats(_state).Turns} 轮");
            if (_settings.AutoRefreshEnabled && _settings.RefreshAfterTurn)
            {
                _postTurnRefreshTimer.Stop();
                _postTurnRefreshTimer.Start();
            }
        }
        _lastKnownTurns = curTurns;

        var primary = WalletStore.PickPrimaryBucket(_state, _settings.PreferredBucketId);
        if (primary?.RemainingPercent is double pct)
        {
            if (pct <= _settings.LowQuotaAlertPct)
            {
                if (announceChanges && !_lowQuotaAlertPlayed)
                {
                    _lowQuotaAlertPlayed = true;
                    PlaySound("quota-low.wav");
                    ShowBubble("quota");
                }
            }
            else
            {
                _lowQuotaAlertPlayed = false;
            }
        }
    }

    public void ApplySettingsToWindow(bool initialPosition)
    {
        Topmost = _settings.AlwaysOnTop;
        HamburgerButton.Visibility = _settings.ShowHamburger ? Visibility.Visible : Visibility.Collapsed;
        WindowScaleTransform.ScaleX = _settings.Scale;
        WindowScaleTransform.ScaleY = _settings.Scale;
        Width = Math.Round(310 * _settings.Scale);
        Height = Math.Round(340 * _settings.Scale);
        _bubbleHideTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.BubbleAutoHideMs, 2000, 30000));
        ConfigureAutoRefreshTimer();
        EnforceAntigravityWindowScope();

        if (initialPosition)
        {
            if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
            {
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
                ClampToVisibleArea();
            }
            else
            {
                ResetWindowPosition(save: false);
            }
            UpdateLeftEdgeFlip();
        }
    }

    private void ClampToVisibleArea()
    {
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth;
        double vb = vt + SystemParameters.VirtualScreenHeight;

        if (Left < vl - Width * 0.4) Left = vl + 16;
        if (Left > vr - Width * 0.6) Left = vr - Width - 16;
        if (Top < vt - Height * 0.3) Top = vt + 16;
        if (Top > vb - Height * 0.6) Top = vb - Height - 16;
    }

    private void ResetWindowPosition(bool save = true)
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 18;
        Top = work.Bottom - Height - 8;
        UpdateLeftEdgeFlip();
        if (save)
        {
            _settings.WindowLeft = Math.Round(Left);
            _settings.WindowTop = Math.Round(Top);
            WalletStore.SaveSettings(_settings);
        }
    }

    private void UpdateLeftEdgeFlip()
    {
        var work = SystemParameters.WorkArea;
        bool onLeft = _settings.FlipOnLeftEdge && (Left <= work.Left + _settings.SnapZonePx + 10);
        MascotFlipTransform.ScaleX = onLeft ? -1 : 1;
    }

    private void SnapToNearestEdge()
    {
        var work = SystemParameters.WorkArea;
        if (_settings.EdgeSnap)
        {
            int zone = Math.Max(8, _settings.SnapZonePx);
            if (Math.Abs(Left - work.Left) <= zone) Left = work.Left;
            else if (Math.Abs((Left + Width) - work.Right) <= zone) Left = work.Right - Width;

            if (Math.Abs(Top - work.Top) <= zone) Top = work.Top;
            else if (Math.Abs((Top + Height) - work.Bottom) <= zone) Top = work.Bottom - Height;
        }
        ClampToVisibleArea();
        UpdateLeftEdgeFlip();

        _settings.WindowLeft = Math.Round(Left);
        _settings.WindowTop = Math.Round(Top);
        WalletStore.SaveSettings(_settings);
    }

    private void UpdateCornerBadgeAndLiveBubble()
    {
        var primary = WalletStore.PickPrimaryBucket(_state, _settings.PreferredBucketId);
        if (primary?.RemainingPercent is double pct)
        {
            CornerQuotaText.Text = $"✦ {pct:0.#}%";
            CornerQuotaText.Foreground = GetToneBrush(pct);
            var secs = WalletStore.GetLiveResetSeconds(primary);
            if (_settings.AutoRefreshEnabled && secs.HasValue && secs.Value <= 0 &&
                (DateTimeOffset.UtcNow - _lastExpiredAutoRefresh).TotalSeconds > 90)
            {
                _lastExpiredAutoRefresh = DateTimeOffset.UtcNow;
                _ = TriggerAgySyncAsync(force: false, showBubbleOnComplete: false);
            }
        }
        else
        {
            var turns = WalletStore.GetTodayStats(_state).Turns;
            CornerQuotaText.Text = turns > 0 ? $"✦ {turns}轮" : "✦ 谷谷歌歌";
            CornerQuotaText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xED, 0xE9, 0xFF));
        }

        if (BubbleContainer.Visibility == Visibility.Visible && BubbleProgressTrack.Visibility == Visibility.Visible)
        {
            PopulateQuotaBubble(primary);
        }
    }

    private SolidColorBrush GetToneBrush(double pct)
    {
        if (pct <= _settings.LowQuotaAlertPct)
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x7C, 0x9D));
        if (pct <= 50)
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD4, 0x66));
        return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0xF0, 0xD0));
    }

    private void PopulateQuotaBubble(QuotaBucket? primary)
    {
        if (primary?.RemainingPercent is double pct)
        {
            BubbleTagText.Text = WalletStore.FriendlyLabel(primary.Id);
            BubbleBadgeText.Text = primary.Source == "agy-statusline" ? "agy 实时" : "手动校准";
            BubbleMainText.Text = $"{pct:0.0}%";
            BubbleMainText.FontSize = 22;
            BubbleMainText.Foreground = GetToneBrush(pct);

            BubbleProgressTrack.Visibility = Visibility.Visible;
            BubbleProgressFill.Width = Math.Max(8, Math.Round(186 * Math.Clamp(pct, 0, 100) / 100.0));
            BubbleProgressFill.Background = pct <= _settings.LowQuotaAlertPct
                ? new LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x5D, 0x8F),
                    System.Windows.Media.Color.FromRgb(0xFF, 0x8B, 0xA7), 0)
                : pct <= 50
                    ? new LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0xBE, 0x55),
                        System.Windows.Media.Color.FromRgb(0xFF, 0xD4, 0x66), 0)
                    : new LinearGradientBrush(
                        System.Windows.Media.Color.FromRgb(0x72, 0xE6, 0xFF),
                        System.Windows.Media.Color.FromRgb(0x8E, 0xF0, 0xD0), 0);

            var secs = WalletStore.GetLiveResetSeconds(primary);
            var modelStr = string.IsNullOrWhiteSpace(_state.ActiveModel) ? "" : $" · {_state.ActiveModel}";
            BubbleSubText.Text = $"{WalletStore.FormatResetPhrase(secs)}{modelStr}";
        }
        else
        {
            BubbleTagText.Text = "Gemini 额度猫娘";
            BubbleBadgeText.Text = "待同步";
            BubbleMainText.Text = "谷谷歌歌…还没收到额度喵";
            BubbleMainText.FontSize = 13;
            BubbleMainText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF3, 0xFF));
            BubbleProgressTrack.Visibility = Visibility.Collapsed;
            BubbleSubText.Text = "右键猫娘可「手动校准额度」或接入 agy";
        }
    }

    private string PickWeightedQuote()
    {
        var primary = WalletStore.PickPrimaryBucket(_state, _settings.PreferredBucketId);
        string mood = "any";
        if (primary?.RemainingPercent is double pct)
        {
            mood = pct <= _settings.LowQuotaAlertPct ? "low" : pct <= 50 ? "mid" : "high";
        }

        var lines = _settings.CustomLines?.Count > 0 ? _settings.CustomLines : WalletSettings.DefaultLines();
        var eligible = lines.Where(l => string.IsNullOrWhiteSpace(l.Mood) || l.Mood == "any" || l.Mood == mood).ToList();
        if (eligible.Count == 0) eligible = lines;
        var nonRepeat = eligible.Where(l => l.Text != _lastQuote).ToList();
        var pool = nonRepeat.Count > 0 ? nonRepeat : eligible;

        int total = pool.Sum(l => Math.Max(1, l.Weight));
        int roll = _rng.Next(total);
        foreach (var item in pool)
        {
            roll -= Math.Max(1, item.Weight);
            if (roll < 0)
            {
                _lastQuote = item.Text;
                return item.Text;
            }
        }
        _lastQuote = pool[0].Text;
        return pool[0].Text;
    }

    private void ShowBubble(string? modeOverride = null)
    {
        var seq = _settings.ClickSequence?.Count > 0 ? _settings.ClickSequence : new List<string> { "quota", "quote", "stats" };
        var mode = modeOverride ?? seq[_clickIndex % seq.Count];

        if (mode == "quota")
        {
            var primary = WalletStore.PickPrimaryBucket(_state, _settings.PreferredBucketId);
            PopulateQuotaBubble(primary);
        }
        else if (mode == "quote")
        {
            BubbleTagText.Text = "猫娘悄悄话";
            BubbleBadgeText.Text = "谷谷歌歌";
            BubbleMainText.Text = PickWeightedQuote();
            BubbleMainText.FontSize = 13;
            BubbleMainText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF3, 0xFF));
            BubbleProgressTrack.Visibility = Visibility.Collapsed;
            BubbleSubText.Text = string.IsNullOrWhiteSpace(_state.ActiveModel)
                ? "再点一下查看今日统计"
                : $"当前模型：{_state.ActiveModel}";
        }
        else
        {
            var today = WalletStore.GetTodayStats(_state);
            BubbleTagText.Text = "今日反重力统计";
            BubbleBadgeText.Text = "Hooks";
            BubbleMainText.Text = $"今日 {today.Turns} 轮调用";
            BubbleMainText.FontSize = 16;
            BubbleMainText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA4, 0xA1, 0xFA));
            BubbleProgressTrack.Visibility = Visibility.Collapsed;
            BubbleSubText.Text = $"{today.Sessions.Count} 个会话 · 累计 {_state.Stats.TotalTurns} 轮";
        }

        AnimateBubbleIn();
    }

    private void ShowCustomToastBubble(string title, string detail)
    {
        BubbleTagText.Text = title;
        BubbleBadgeText.Text = "实时";
        BubbleMainText.Text = "谷谷歌歌…任务搞定啦喵！";
        BubbleMainText.FontSize = 13;
        BubbleMainText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0xF0, 0xD0));
        BubbleProgressTrack.Visibility = Visibility.Collapsed;
        BubbleSubText.Text = detail;
        AnimateBubbleIn();
    }

    private void AnimateBubbleIn()
    {
        BubbleContainer.Visibility = Visibility.Visible;
        var dur = TimeSpan.FromMilliseconds(210);
        var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
        BubbleContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur));
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1.0, dur) { EasingFunction = ease });
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1.0, dur) { EasingFunction = ease });
        BubbleTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, dur) { EasingFunction = ease });

        _bubbleHideTimer.Stop();
        _bubbleHideTimer.Start();
    }

    private void HideBubble()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (BubbleContainer.Opacity < 0.05)
                BubbleContainer.Visibility = Visibility.Collapsed;
        };
        BubbleContainer.BeginAnimation(OpacityProperty, fade);
    }

    private void BubbleContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHoveringBubble = true;
        _bubbleHideTimer.Stop();
    }

    private void BubbleContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHoveringBubble = false;
        _bubbleHideTimer.Stop();
        _bubbleHideTimer.Start();
    }

    private void AnimateSquishDown()
    {
        var dur = TimeSpan.FromMilliseconds(90);
        MascotSquishTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.10, dur));
        MascotSquishTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, dur));
    }

    private void AnimateBounceUp()
    {
        var kfX = new DoubleAnimationUsingKeyFrames();
        kfX.KeyFrames.Add(new EasingDoubleKeyFrame(0.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
        kfX.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(210))));
        kfX.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));

        var kfY = new DoubleAnimationUsingKeyFrames();
        kfY.KeyFrames.Add(new EasingDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
        kfY.KeyFrames.Add(new EasingDoubleKeyFrame(0.98, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(210))));
        kfY.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));

        MascotSquishTransform.BeginAnimation(ScaleTransform.ScaleXProperty, kfX);
        MascotSquishTransform.BeginAnimation(ScaleTransform.ScaleYProperty, kfY);
    }

    public void PlaySound(string wavFileName)
    {
        if (!_settings.SoundEnabled || _settings.SoundVolume <= 0.01) return;
        var fullPath = Path.Combine(_assetsDir, "sounds", wavFileName);
        if (!File.Exists(fullPath)) return;
        try
        {
            _activePlayer?.Close();
            _activePlayer = new MediaPlayer { Volume = Math.Clamp(_settings.SoundVolume, 0, 1) };
            _activePlayer.Open(new Uri(fullPath, UriKind.Absolute));
            _activePlayer.Play();
        }
        catch { }
    }

    private void MascotHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPointerDown = true;
        _isDragging = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _winStartLeft = Left;
        _winStartTop = Top;
        MascotHost.CaptureMouse();
        AnimateSquishDown();
        PlaySound("squish-down.wav");
    }

    private void MascotHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPointerDown) return;
        var cur = PointToScreen(e.GetPosition(this));
        var dx = cur.X - _dragStartScreen.X;
        var dy = cur.Y - _dragStartScreen.Y;
        if (!_isDragging && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5))
        {
            _isDragging = true;
        }
        if (_isDragging)
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            Left = _winStartLeft + dx / dpiX;
            Top = _winStartTop + dy / dpiY;
        }
    }

    private void MascotHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerDown) return;
        _isPointerDown = false;
        MascotHost.ReleaseMouseCapture();
        AnimateBounceUp();

        if (_isDragging)
        {
            _isDragging = false;
            SnapToNearestEdge();
        }
        else
        {
            PlaySound("squish-up.wav");
            _clickIndex++;
            ShowBubble();
        }
    }

    private void CornerQuotaPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PlaySound("squish-up.wav");
        ShowBubble("quota");
    }

    private void MascotHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowContextMenu(MascotHost);
    }

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        ShowContextMenu(HamburgerButton);
    }

    private void ShowContextMenu(UIElement target)
    {
        var menu = new ContextMenu();

        var miRefresh = new MenuItem { Header = "🔄 立即从 agy 同步真实额度" };
        miRefresh.Click += async (_, _) => await TriggerAgySyncAsync(force: true, showBubbleOnComplete: true);
        menu.Items.Add(miRefresh);

        var miCal = new MenuItem { Header = "🎯 手动校准剩余额度..." };
        miCal.Click += (_, _) => OpenSettings(0);
        menu.Items.Add(miCal);

        var miSet = new MenuItem { Header = "⚙️ 外观、音效与台词设置..." };
        miSet.Click += (_, _) => OpenSettings(1);
        menu.Items.Add(miSet);

        menu.Items.Add(new Separator());

        var miScope = new MenuItem { Header = _settings.OnlyShowInAntigravity ? "🎯 仅在反重力窗口显示（已开启）" : "🎯 仅在反重力窗口显示（当前全局显示）" };
        miScope.Click += (_, _) =>
        {
            _settings.OnlyShowInAntigravity = !_settings.OnlyShowInAntigravity;
            WalletStore.SaveSettings(_settings);
            ApplySettingsToWindow(false);
        };
        menu.Items.Add(miScope);

        var miTop = new MenuItem { Header = _settings.AlwaysOnTop ? "📌 取消窗口置顶" : "📌 开启窗口置顶" };
        miTop.Click += (_, _) =>
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            WalletStore.SaveSettings(_settings);
            ApplySettingsToWindow(false);
        };
        menu.Items.Add(miTop);

        var miBtn = new MenuItem { Header = _settings.ShowHamburger ? "👁️ 隐藏 ☰ 按钮（右键仍可打开）" : "👁️ 显示 ☰ 按钮" };
        miBtn.Click += (_, _) =>
        {
            _settings.ShowHamburger = !_settings.ShowHamburger;
            WalletStore.SaveSettings(_settings);
            ApplySettingsToWindow(false);
        };
        menu.Items.Add(miBtn);

        var miReset = new MenuItem { Header = "🧭 重置位置到右下角" };
        miReset.Click += (_, _) => ResetWindowPosition();
        menu.Items.Add(miReset);

        menu.Items.Add(new Separator());

        var miHide = new MenuItem { Header = "🌙 隐藏到系统托盘" };
        miHide.Click += (_, _) =>
        {
            _userHiddenToTray = true;
            Hide();
        };
        menu.Items.Add(miHide);

        var miExit = new MenuItem { Header = "❌ 退出挂件" };
        miExit.Click += (_, _) => ExitApplication();
        menu.Items.Add(miExit);

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void OpenSettings(int tabIndex)
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.SelectTab(tabIndex);
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this, tabIndex);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            _userHiddenToTray = true;
            Hide();
        }
        else
        {
            _userHiddenToTray = false;
            _hiddenByAppScope = false;
            Show();
            ShowBubble("quota");
        }
    }

    private void ExitApplication()
    {
        if (_reallyClosing) return;
        _reallyClosing = true;
        _scopeTimer.Stop();
        _autoRefreshTimer.Stop();
        _postTurnRefreshTimer.Stop();
        _tickTimer.Stop();
        try { _ipcCts?.Cancel(); } catch { }
        _wakeEvent?.Dispose();
        _quitEvent?.Dispose();
        _ipcCts?.Dispose();
        _watcher?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _settingsWindow?.Close();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }
}

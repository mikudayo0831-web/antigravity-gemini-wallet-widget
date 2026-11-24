using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GeminiWalletWidget;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _ownerWidget;
    private WalletSettings _settings = new();
    private bool _initializing = true;

    public SettingsWindow(MainWindow ownerWidget, int initialTabIndex = 0)
    {
        InitializeComponent();
        _ownerWidget = ownerWidget;
        LoadDataIntoUi();
        SelectTab(initialTabIndex);
        _initializing = false;
    }

    public void SelectTab(int index)
    {
        if (index >= 0 && index < MainTabs.Items.Count)
            MainTabs.SelectedIndex = index;
    }

    private void LoadDataIntoUi()
    {
        _settings = WalletStore.LoadSettings();
        var state = WalletStore.LoadState();
        var primary = WalletStore.PickPrimaryBucket(state, _settings.PreferredBucketId);
        var today = WalletStore.GetTodayStats(state);

        if (primary?.RemainingPercent is double pct)
        {
            var secs = WalletStore.GetLiveResetSeconds(primary);
            var src = primary.Source == "agy-statusline" ? "agy statusLine 实时" : "手动校准";
            var extraLines = state.Quotas.Values
                .Where(b => b != null && b.RemainingPercent.HasValue)
                .OrderBy(b => b.Id)
                .Select(b => $"  • {WalletStore.FriendlyLabel(b.Id)}: {b.RemainingPercent:0.1}% ({WalletStore.FormatResetPhrase(WalletStore.GetLiveResetSeconds(b))})");
            LiveStatusSummary.Text =
                $"主窗口：{WalletStore.FriendlyLabel(primary.Id)} {pct:0.1}%（{src}）\n" +
                string.Join("\n", extraLines) + "\n" +
                $"最近模型：{state.ActiveModel ?? "未记录"} · 今日对话：{today.Turns} 轮（累计 {state.Stats.TotalTurns} 轮）";
            CalPctSlider.Value = pct;
            CalPctBox.Text = pct.ToString("0.#");
        }
        else
        {
            LiveStatusSummary.Text =
                $"暂未收到 Gemini 额度百分比数据。\n" +
                $"最近模型：{state.ActiveModel ?? "未记录"} · 今日对话：{today.Turns} 轮（累计 {state.Stats.TotalTurns} 轮）\n" +
                $"可点击上方「立即从 agy 后台同步」，或在下方手动校准。";
        }

        // Sync preferences
        string prefId = _settings.PreferredBucketId ?? "";
        for (int i = 0; i < PreferredBucketCombo.Items.Count; i++)
        {
            if ((PreferredBucketCombo.Items[i] as ComboBoxItem)?.Tag?.ToString() == prefId)
            {
                PreferredBucketCombo.SelectedIndex = i;
                break;
            }
        }
        AutoRefreshCheck.IsChecked = _settings.AutoRefreshEnabled;
        RefreshMinsBox.Text = Math.Clamp(_settings.AutoRefreshMinutes, 1, 60).ToString();
        RefreshAfterTurnCheck.IsChecked = _settings.RefreshAfterTurn;

        OnlyInAgyCheck.IsChecked = _settings.OnlyShowInAntigravity;
        AutoLaunchCheck.IsChecked = _settings.AutoLaunchWithAntigravity;
        AutoCloseCheck.IsChecked = _settings.AutoCloseWithAntigravity;
        ScaleSlider.Value = _settings.Scale;
        ScaleLabel.Text = $"{Math.Round(_settings.Scale * 100)}%";
        AlwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;
        EdgeSnapCheck.IsChecked = _settings.EdgeSnap;
        FlipLeftCheck.IsChecked = _settings.FlipOnLeftEdge;
        ShowHamburgerCheck.IsChecked = _settings.ShowHamburger;
        SoundEnabledCheck.IsChecked = _settings.SoundEnabled;
        VolumeSlider.Value = Math.Round(_settings.SoundVolume * 100);
        VolumeLabel.Text = $"{Math.Round(_settings.SoundVolume * 100)}%";
        TurnChimeCheck.IsChecked = _settings.TurnDoneChime;
        LowAlertBox.Text = _settings.LowQuotaAlertPct.ToString();
        AutoHideSecBox.Text = Math.Max(2, (int)Math.Round(_settings.BubbleAutoHideMs / 1000.0)).ToString();

        CustomLinesBox.Text = string.Join(
            Environment.NewLine,
            (_settings.CustomLines?.Count > 0 ? _settings.CustomLines : WalletSettings.DefaultLines())
                .Select(l => $"{l.Text} | {l.Weight} | {l.Mood}")
        );
    }

    private void CalPctSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CalPctBox != null)
            CalPctBox.Text = Math.Round(e.NewValue, 1).ToString("0.#");
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleLabel != null)
            ScaleLabel.Text = $"{Math.Round(e.NewValue * 100)}%";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel != null)
            VolumeLabel.Text = $"{Math.Round(e.NewValue)}%";
    }

    private async void SyncAgyNow_Click(object sender, RoutedEventArgs e)
    {
        SyncAgyNowBtn.IsEnabled = false;
        StatusNotice.Text = "⏳ 正在后台通过 agy 同步最新真实额度...";
        try
        {
            var (ok, msg) = await _ownerWidget.TriggerAgySyncAsync(force: true, showBubbleOnComplete: true);
            LoadDataIntoUi();
            StatusNotice.Text = ok ? msg : $"同步失败：{msg}";
        }
        finally
        {
            SyncAgyNowBtn.IsEnabled = true;
        }
    }

    private void SaveSyncPrefs_Click(object sender, RoutedEventArgs e)
    {
        _settings.PreferredBucketId = (PreferredBucketCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        _settings.AutoRefreshEnabled = AutoRefreshCheck.IsChecked == true;
        if (int.TryParse(RefreshMinsBox.Text.Trim(), out int mins))
            _settings.AutoRefreshMinutes = Math.Clamp(mins, 1, 60);
        _settings.RefreshAfterTurn = RefreshAfterTurnCheck.IsChecked == true;

        WalletStore.SaveSettings(_settings);
        _ownerWidget.ReloadFromDisk(announceChanges: false);
        LoadDataIntoUi();
        StatusNotice.Text = "✨ 已保存主显示窗口与后台自动同步设置！";
    }

    private void SaveCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(CalPctBox.Text.Trim(), out double pct) || pct < 0 || pct > 100)
        {
            StatusNotice.Text = "请输入 0 到 100 之间的有效百分比";
            return;
        }
        int? hrs = int.TryParse(CalHoursBox.Text.Trim(), out int h) && h >= 0 ? h : null;
        int? mins = int.TryParse(CalMinsBox.Text.Trim(), out int m) && m >= 0 ? m : null;
        string bucketId = (CalBucketCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "gemini-weekly";

        WalletStore.CalibrateQuota(bucketId, pct, hrs, mins);
        _ownerWidget.ReloadFromDisk(announceChanges: false);
        LoadDataIntoUi();
        StatusNotice.Text = "✨ 已保存 Gemini 额度校准并同步！";
    }

    private void SetupAgyStatusLine_Click(object sender, RoutedEventArgs e)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "scripts", "setup-agy-statusline.ps1")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "scripts", "setup-agy-statusline.ps1")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "setup-agy-statusline.ps1")),
            Path.Combine(AppContext.BaseDirectory, "scripts", "setup-agy-statusline.ps1"),
        };
        var scriptPath = candidates.FirstOrDefault(File.Exists);
        if (scriptPath == null)
        {
            StatusNotice.Text = "未找到 setup-agy-statusline.ps1 脚本";
            return;
        }
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var proc = Process.Start(psi);
            string stdout = proc?.StandardOutput.ReadToEnd()?.Trim() ?? "";
            proc?.WaitForExit(8000);
            if (proc != null && proc.ExitCode == 0)
            {
                var parts = stdout.Split('|', 2);
                string msg = parts.Length == 2 ? parts[1] : "已写入 ~/.gemini/antigravity-cli/settings.json";
                StatusNotice.Text = parts.Length == 2 && parts[0] == "AGY_MISSING"
                    ? "⚠️ 本机未安装 agy CLI（详见弹窗说明）"
                    : "✨ 已成功配置 agy statusLine！";
                if (parts.Length == 2 && parts[0] == "AGY_MISSING")
                {
                    System.Windows.MessageBox.Show(
                        "已为你写入 ~/.gemini/antigravity-cli/settings.json。\n\n" +
                        "但检测到你当前电脑未安装/未运行 Antigravity CLI (agy) 终端命令：\n" +
                        "• Antigravity 桌面客户端（GUI）目前没有开放给插件的实时额度接口，不会触发 agy statusLine；\n" +
                        "• 桌面端使用请直接在上方「手动校准 Gemini 剩余额度」填写当前剩余百分比与重置时间，猫娘会实时倒计时；\n" +
                        "• 如果你在终端安装了 agy CLI 并运行 agy 对话，猫娘会自动接管并实时更新额度。",
                        "关于 agy 实时同步的说明",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusNotice.Text = "✨ " + msg;
                }
            }
            else
                StatusNotice.Text = "配置脚本返回异常，请检查 PowerShell 输出";
        }
        catch (Exception ex)
        {
            StatusNotice.Text = $"配置失败：{ex.Message}";
        }
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.SoundEnabled = SoundEnabledCheck.IsChecked == true;
        _settings.SoundVolume = Math.Clamp(VolumeSlider.Value / 100.0, 0, 1);
        WalletStore.SaveSettings(_settings);
        _ownerWidget.ReloadFromDisk(false);
        _ownerWidget.PlaySound("squish-up.wav");
    }

    private void SaveGeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.OnlyShowInAntigravity = OnlyInAgyCheck.IsChecked == true;
        _settings.AutoLaunchWithAntigravity = AutoLaunchCheck.IsChecked == true;
        _settings.AutoCloseWithAntigravity = AutoCloseCheck.IsChecked == true;
        _settings.Scale = Math.Round(ScaleSlider.Value, 2);
        _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _settings.EdgeSnap = EdgeSnapCheck.IsChecked == true;
        _settings.FlipOnLeftEdge = FlipLeftCheck.IsChecked == true;
        _settings.ShowHamburger = ShowHamburgerCheck.IsChecked == true;
        _settings.SoundEnabled = SoundEnabledCheck.IsChecked == true;
        _settings.SoundVolume = Math.Round(VolumeSlider.Value / 100.0, 2);
        _settings.TurnDoneChime = TurnChimeCheck.IsChecked == true;

        if (int.TryParse(LowAlertBox.Text.Trim(), out int low))
            _settings.LowQuotaAlertPct = Math.Clamp(low, 1, 95);
        if (int.TryParse(AutoHideSecBox.Text.Trim(), out int sec))
            _settings.BubbleAutoHideMs = Math.Clamp(sec, 2, 30) * 1000;

        WalletStore.SaveSettings(_settings);
        _ownerWidget.ReloadFromDisk(false);
        StatusNotice.Text = "✨ 外观与交互设置已保存！";
    }

    private void RestoreDefaultLines_Click(object sender, RoutedEventArgs e)
    {
        CustomLinesBox.Text = string.Join(
            Environment.NewLine,
            WalletSettings.DefaultLines().Select(l => $"{l.Text} | {l.Weight} | {l.Mood}")
        );
        StatusNotice.Text = "已恢复默认谷谷歌歌台词，点击「保存台词库」生效";
    }

    private void SaveLines_Click(object sender, RoutedEventArgs e)
    {
        var parsed = new List<CustomLineItem>();
        foreach (var raw in CustomLinesBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var parts = trimmed.Split('|').Select(p => p.Trim()).ToArray();
            int weight = parts.Length >= 2 && int.TryParse(parts[1], out int w) ? Math.Max(1, w) : 20;
            string mood = parts.Length >= 3 && new[] { "any", "high", "mid", "low" }.Contains(parts[2]) ? parts[2] : "any";
            parsed.Add(new CustomLineItem { Text = parts[0], Weight = weight, Mood = mood });
        }

        if (parsed.Count == 0)
        {
            StatusNotice.Text = "至少需要保留一条台词哦";
            return;
        }

        _settings.CustomLines = parsed;
        WalletStore.SaveSettings(_settings);
        _ownerWidget.ReloadFromDisk(false);
        StatusNotice.Text = $"✨ 已保存 {parsed.Count} 条自定义猫娘台词！";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

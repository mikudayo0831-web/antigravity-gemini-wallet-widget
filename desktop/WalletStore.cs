using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace GeminiWalletWidget;

public static class WalletStore
{
    private static readonly SemaphoreSlim AgySyncGate = new(1, 1);
    private static DateTimeOffset _lastAgySyncAttempt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string RuntimeDir =>
        Environment.GetEnvironmentVariable("AGY_GEMINI_WALLET_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-gemini-wallet");

    public static string StatePath => Path.Combine(RuntimeDir, "state.json");
    public static string SettingsPath => Path.Combine(RuntimeDir, "settings.json");

    public static void EnsureDir() => Directory.CreateDirectory(RuntimeDir);

    public static WalletSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var raw = File.ReadAllText(SettingsPath);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var s = JsonSerializer.Deserialize<WalletSettings>(raw, JsonOpts);
                    if (s != null)
                    {
                        if (s.CustomLines == null || s.CustomLines.Count == 0)
                            s.CustomLines = WalletSettings.DefaultLines();
                        if (s.ClickSequence == null || s.ClickSequence.Count == 0)
                            s.ClickSequence = new() { "quota", "quote", "stats" };
                        s.Scale = Math.Clamp(s.Scale, 0.6, 1.6);
                        s.SoundVolume = Math.Clamp(s.SoundVolume, 0.0, 1.0);
                        s.LowQuotaAlertPct = Math.Clamp(s.LowQuotaAlertPct, 1, 95);
                        return s;
                    }
                }
            }
        }
        catch { }
        return new WalletSettings();
    }

    public static void SaveSettings(WalletSettings settings)
    {
        EnsureDir();
        WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    public static WalletState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var raw = File.ReadAllText(StatePath);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var st = JsonSerializer.Deserialize<WalletState>(raw, JsonOpts);
                    if (st != null)
                    {
                        st.Quotas ??= new();
                        st.Stats ??= new();
                        st.Stats.Daily ??= new();
                        return st;
                    }
                }
            }
        }
        catch { }
        return new WalletState();
    }

    public static void SaveState(WalletState state)
    {
        EnsureDir();
        state.UpdatedAt = DateTime.UtcNow.ToString("O");
        WriteAtomic(StatePath, JsonSerializer.Serialize(state, JsonOpts));
    }

    public static void CalibrateQuota(string bucketId, double remainingPercent, int? resetHours, int? resetMinutes)
    {
        var state = LoadState();
        var pct = Math.Round(Math.Clamp(remainingPercent, 0, 100), 1);
        long? resetSecs = null;
        if (resetHours.HasValue || resetMinutes.HasValue)
        {
            long total = (long)(resetHours ?? 0) * 3600 + (long)(resetMinutes ?? 0) * 60;
            if (total > 0) resetSecs = total;
        }
        string? resetTime = resetSecs.HasValue
            ? DateTime.UtcNow.AddSeconds(resetSecs.Value).ToString("O")
            : null;

        var id = string.IsNullOrWhiteSpace(bucketId) ? "gemini-weekly" : bucketId.Trim();
        state.Quotas[id] = new QuotaBucket
        {
            Id = id,
            Label = FriendlyLabel(id),
            RemainingFraction = pct / 100.0,
            RemainingPercent = pct,
            ResetTime = resetTime,
            ResetInSeconds = resetSecs,
            Disabled = false,
            Source = "manual",
            UpdatedAt = DateTime.UtcNow.ToString("O"),
        };
        if (state.Source == "none" || state.Source == "hooks-only")
            state.Source = "manual";

        SaveState(state);
    }

    public static string FriendlyLabel(string bucketId)
    {
        var id = bucketId.ToLowerInvariant();
        if (id == "gemini-5h") return "Gemini 5小时额度";
        if (id == "gemini-weekly") return "Gemini 周额度";
        if (id.Contains("argon")) return "Gemini Argon 周额度";
        if (id == "3p-5h") return "第三方模型 5小时额度";
        if (id == "3p-weekly") return "第三方模型 周额度";
        if (id.Contains("gemini") && id.Contains("week")) return $"Gemini 周额度 ({bucketId})";
        if (id.Contains("gemini") && (id.Contains("5h") || id.Contains("hour") || id.Contains("short"))) return "Gemini 5小时额度";
        if (id.Contains("gemini") && id.Contains("pro")) return "Gemini Pro 额度";
        if (id.Contains("gemini") && id.Contains("flash")) return "Gemini Flash 额度";
        if (id.Contains("gemini")) return $"Gemini ({bucketId})";
        return bucketId;
    }

    public static string? FindAgyExe()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("AGY_BIN_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe"),
        };
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
        }
        return null;
    }

    public static string? DetectSystemProxyUrl()
    {
        var existing = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                    ?? Environment.GetEnvironmentVariable("https_proxy")
                    ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                    ?? Environment.GetEnvironmentVariable("http_proxy");
        if (!string.IsNullOrWhiteSpace(existing)) return existing.Trim();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key == null) return null;
            var enabled = key.GetValue("ProxyEnable");
            if (enabled is not int en || en != 1) return null;
            var raw = key.GetValue("ProxyServer")?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (raw.Contains('='))
            {
                foreach (var seg in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (seg.StartsWith("https=", StringComparison.OrdinalIgnoreCase))
                    {
                        raw = seg.Substring(6).Trim();
                        break;
                    }
                    if (seg.StartsWith("http=", StringComparison.OrdinalIgnoreCase))
                    {
                        raw = seg.Substring(5).Trim();
                    }
                }
            }
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "http://" + raw;
            }
            return raw;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(bool Ok, string Message)> SyncWithAgyAsync(bool force = false)
    {
        var exe = FindAgyExe();
        if (exe == null)
            return (false, "未找到 agy.exe");

        if (!force && (DateTimeOffset.UtcNow - _lastAgySyncAttempt).TotalSeconds < 12)
            return (true, "刚刚已同步");

        if (!await AgySyncGate.WaitAsync(0))
            return (true, "正在同步中");

        try
        {
            _lastAgySyncAttempt = DateTimeOffset.UtcNow;
            var psi = new ProcessStartInfo(exe, "models")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proxy = DetectSystemProxyUrl();
            if (!string.IsNullOrWhiteSpace(proxy))
            {
                psi.Environment["HTTP_PROXY"] = proxy;
                psi.Environment["HTTPS_PROXY"] = proxy;
            }

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "无法启动 agy.exe");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, "agy 同步超时，请检查代理连接");
            }

            if (proc.ExitCode == 0)
                return (true, "已从 agy 同步最新真实额度 ✨");

            var err = await proc.StandardError.ReadToEndAsync();
            return (false, string.IsNullOrWhiteSpace(err) ? $"agy 退出码 {proc.ExitCode}" : err.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            AgySyncGate.Release();
        }
    }

    public static QuotaBucket? PickPrimaryBucket(WalletState state, string preferredId)
    {
        var valid = state.Quotas.Values
            .Where(b => b != null && !b.Disabled && b.RemainingPercent.HasValue)
            .ToList();
        if (valid.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(preferredId) &&
            state.Quotas.TryGetValue(preferredId, out var pref) &&
            !pref.Disabled && pref.RemainingPercent.HasValue)
            return pref;

        var gemini = valid.Where(b => b.Id.Contains("gemini", StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = gemini.Count > 0 ? gemini : valid;
        var live = pool.Where(b => b.Source == "agy-statusline").ToList();
        var candidates = live.Count > 0 ? live : pool;

        return candidates.FirstOrDefault(b => b.Id.Contains("5h", StringComparison.OrdinalIgnoreCase) || b.Id.Contains("hour", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(b => b.Id.Contains("week", StringComparison.OrdinalIgnoreCase))
            ?? candidates[0];
    }

    public static long? GetLiveResetSeconds(QuotaBucket? bucket)
    {
        if (bucket == null) return null;
        if (!string.IsNullOrWhiteSpace(bucket.ResetTime) &&
            DateTimeOffset.TryParse(bucket.ResetTime, out var target))
        {
            return Math.Max(0, (long)Math.Round((target - DateTimeOffset.UtcNow).TotalSeconds));
        }
        if (bucket.ResetInSeconds.HasValue)
        {
            long elapsed = 0;
            if (!string.IsNullOrWhiteSpace(bucket.UpdatedAt) &&
                DateTimeOffset.TryParse(bucket.UpdatedAt, out var updated))
            {
                elapsed = Math.Max(0, (long)Math.Round((DateTimeOffset.UtcNow - updated).TotalSeconds));
            }
            return Math.Max(0, bucket.ResetInSeconds.Value - elapsed);
        }
        return null;
    }

    public static string FormatCountdown(long? seconds)
    {
        if (!seconds.HasValue) return "未知";
        if (seconds.Value <= 0) return "已到重置时间";
        long d = seconds.Value / 86400;
        long h = (seconds.Value % 86400) / 3600;
        long m = (seconds.Value % 3600) / 60;
        if (d > 0) return $"{d}天 {h}小时";
        if (h > 0) return $"{h}小时 {m}分";
        return $"{Math.Max(1, m)}分钟";
    }

    public static string FormatResetPhrase(long? seconds)
    {
        if (!seconds.HasValue) return "点击猫娘切换台词";
        if (seconds.Value <= 0) return "已过重置时间 · 点击刷新同步";
        return $"{FormatCountdown(seconds)} 后重置";
    }

    public static DailyStats GetTodayStats(WalletState state)
    {
        string key = DateTime.Now.ToString("yyyy-MM-dd");
        return state.Stats.Daily.TryGetValue(key, out var d) ? d : new DailyStats();
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = $"{path}.{Environment.ProcessId}.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.tmp";
        File.WriteAllText(tmp, content, System.Text.Encoding.UTF8);
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            File.Copy(tmp, path, overwrite: true);
            try { File.Delete(tmp); } catch { }
        }
    }
}

using System.Text.Json.Serialization;

namespace GeminiWalletWidget;

public class CustomLineItem
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 20;

    [JsonPropertyName("mood")]
    public string Mood { get; set; } = "any";
}

public class WalletSettings
{
    [JsonPropertyName("autoLaunchWithAntigravity")]
    public bool AutoLaunchWithAntigravity { get; set; } = true;

    [JsonPropertyName("autoCloseWithAntigravity")]
    public bool AutoCloseWithAntigravity { get; set; } = false;

    [JsonPropertyName("onlyShowInAntigravity")]
    public bool OnlyShowInAntigravity { get; set; } = true;

    [JsonPropertyName("autoRefreshEnabled")]
    public bool AutoRefreshEnabled { get; set; } = true;

    [JsonPropertyName("autoRefreshMinutes")]
    public int AutoRefreshMinutes { get; set; } = 5;

    [JsonPropertyName("refreshAfterTurn")]
    public bool RefreshAfterTurn { get; set; } = true;

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 1.0;

    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; } = true;

    [JsonPropertyName("edgeSnap")]
    public bool EdgeSnap { get; set; } = true;

    [JsonPropertyName("snapZonePx")]
    public int SnapZonePx { get; set; } = 44;

    [JsonPropertyName("flipOnLeftEdge")]
    public bool FlipOnLeftEdge { get; set; } = true;

    [JsonPropertyName("showHamburger")]
    public bool ShowHamburger { get; set; } = true;

    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; set; } = true;

    [JsonPropertyName("soundVolume")]
    public double SoundVolume { get; set; } = 0.7;

    [JsonPropertyName("turnDoneChime")]
    public bool TurnDoneChime { get; set; } = true;

    [JsonPropertyName("lowQuotaAlertPct")]
    public int LowQuotaAlertPct { get; set; } = 20;

    [JsonPropertyName("bubbleAutoHideMs")]
    public int BubbleAutoHideMs { get; set; } = 5200;

    [JsonPropertyName("preferredBucketId")]
    public string PreferredBucketId { get; set; } = "";

    [JsonPropertyName("clickSequence")]
    public List<string> ClickSequence { get; set; } = new() { "quota", "quote", "stats" };

    [JsonPropertyName("windowLeft")]
    public double WindowLeft { get; set; } = -1;

    [JsonPropertyName("windowTop")]
    public double WindowTop { get; set; } = -1;

    [JsonPropertyName("customLines")]
    public List<CustomLineItem> CustomLines { get; set; } = DefaultLines();

    public static List<CustomLineItem> DefaultLines() => new()
    {
        new() { Text = "谷谷歌歌…今天的 Gemini 额度也很充足喵！", Weight = 30, Mood = "high" },
        new() { Text = "谷谷歌歌…捏一下耳朵就会涨灵感哦 ✨", Weight = 25, Mood = "any" },
        new() { Text = "谷谷歌歌…反重力充能中，随时可以开工！", Weight = 20, Mood = "any" },
        new() { Text = "谷谷歌歌…额度掉到一半以下啦，省着点吃喵", Weight = 25, Mood = "mid" },
        new() { Text = "谷谷歌歌…快没额度啦，记得看看重置倒计时！", Weight = 35, Mood = "low" },
        new() { Text = "谷谷歌歌…摸鱼也要记得保存代码喵~", Weight = 15, Mood = "any" },
        new() { Text = "谷谷歌歌…异色瞳正在帮你盯着 Gemini 余额！", Weight = 20, Mood = "any" },
    };
}

public class QuotaBucket
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("remainingFraction")]
    public double? RemainingFraction { get; set; }

    [JsonPropertyName("remainingPercent")]
    public double? RemainingPercent { get; set; }

    [JsonPropertyName("remainingAmount")]
    public double? RemainingAmount { get; set; }

    [JsonPropertyName("resetTime")]
    public string? ResetTime { get; set; }

    [JsonPropertyName("resetInSeconds")]
    public long? ResetInSeconds { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "agy-statusline";

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}

public class DailyStats
{
    [JsonPropertyName("turns")]
    public int Turns { get; set; }

    [JsonPropertyName("models")]
    public Dictionary<string, int> Models { get; set; } = new();

    [JsonPropertyName("sessions")]
    public List<string> Sessions { get; set; } = new();
}

public class WalletStats
{
    [JsonPropertyName("totalTurns")]
    public int TotalTurns { get; set; }

    [JsonPropertyName("daily")]
    public Dictionary<string, DailyStats> Daily { get; set; } = new();

    [JsonPropertyName("lastTurnAt")]
    public string? LastTurnAt { get; set; }

    [JsonPropertyName("lastTerminationReason")]
    public string? LastTerminationReason { get; set; }
}

public class WalletState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "none";

    [JsonPropertyName("planTier")]
    public string? PlanTier { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("agentState")]
    public string? AgentState { get; set; }

    [JsonPropertyName("activeModel")]
    public string? ActiveModel { get; set; }

    [JsonPropertyName("activeConversationId")]
    public string? ActiveConversationId { get; set; }

    [JsonPropertyName("activeConversationTitle")]
    public string? ActiveConversationTitle { get; set; }

    [JsonPropertyName("quotas")]
    public Dictionary<string, QuotaBucket> Quotas { get; set; } = new();

    [JsonPropertyName("stats")]
    public WalletStats Stats { get; set; } = new();
}

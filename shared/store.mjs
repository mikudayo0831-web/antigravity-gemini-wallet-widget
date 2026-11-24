import fs from "node:fs";
import os from "node:os";
import path from "node:path";

export const RUNTIME_DIR =
  process.env.AGY_GEMINI_WALLET_DIR ||
  path.join(os.homedir(), ".gemini", "antigravity-gemini-wallet");

export const STATE_PATH = path.join(RUNTIME_DIR, "state.json");
export const SETTINGS_PATH = path.join(RUNTIME_DIR, "settings.json");

export const DEFAULT_CUSTOM_LINES = [
  { text: "谷谷歌歌…今天的 Gemini 额度也很充足喵！", weight: 30, mood: "high" },
  { text: "谷谷歌歌…捏一下耳朵就会涨灵感哦 ✨", weight: 25, mood: "any" },
  { text: "谷谷歌歌…反重力充能中，随时可以开工！", weight: 20, mood: "any" },
  { text: "谷谷歌歌…额度掉到一半以下啦，省着点吃喵", weight: 25, mood: "mid" },
  { text: "谷谷歌歌…快没额度啦，记得看看重置倒计时！", weight: 35, mood: "low" },
  { text: "谷谷歌歌…摸鱼也要记得保存代码喵~", weight: 15, mood: "any" },
  { text: "谷谷歌歌…异色瞳正在帮你盯着 Gemini 余额！", weight: 20, mood: "any" },
];

export const DEFAULT_SETTINGS = {
  autoLaunchWithAntigravity: true,
  autoCloseWithAntigravity: false,
  onlyShowInAntigravity: true,
  autoRefreshEnabled: true,
  autoRefreshMinutes: 5,
  refreshAfterTurn: true,
  scale: 1.0,
  alwaysOnTop: true,
  edgeSnap: true,
  snapZonePx: 44,
  flipOnLeftEdge: true,
  showHamburger: true,
  soundEnabled: true,
  soundVolume: 0.7,
  turnDoneChime: true,
  lowQuotaAlertPct: 20,
  bubbleAutoHideMs: 5200,
  preferredBucketId: "",
  clickSequence: ["quota", "quote", "stats"],
  windowLeft: -1,
  windowTop: -1,
  customLines: DEFAULT_CUSTOM_LINES,
};

export function createEmptyState() {
  return {
    version: 1,
    updatedAt: null,
    source: "none", // "agy-statusline" | "manual" | "hooks-only" | "none"
    planTier: null,
    email: null,
    agentState: null,
    activeModel: null,
    activeConversationId: null,
    activeConversationTitle: null,
    quotas: {}, // bucketId -> { id, label, remainingFraction, remainingPercent, remainingAmount, resetTime, resetInSeconds, source, updatedAt, disabled }
    contextWindow: null,
    cost: null,
    manualCalibration: null, // { bucketId, label, remainingPercent, resetAtIso, updatedAt }
    stats: {
      totalTurns: 0,
      daily: {}, // "YYYY-MM-DD" -> { turns, models: { [model]: count }, sessions: [id...] }
      lastTurnAt: null,
      lastTerminationReason: null,
    },
  };
}

export function ensureRuntimeDir() {
  fs.mkdirSync(RUNTIME_DIR, { recursive: true });
}

function readJsonSafe(filePath, fallback) {
  try {
    if (!fs.existsSync(filePath)) return fallback;
    const raw = fs.readFileSync(filePath, "utf8");
    if (!raw.trim()) return fallback;
    return JSON.parse(raw);
  } catch {
    return fallback;
  }
}

export function writeJsonAtomic(filePath, data) {
  ensureRuntimeDir();
  const tmpPath = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  fs.writeFileSync(tmpPath, JSON.stringify(data, null, 2), "utf8");
  try {
    fs.renameSync(tmpPath, filePath);
  } catch {
    fs.copyFileSync(tmpPath, filePath);
    try {
      fs.unlinkSync(tmpPath);
    } catch {
      // ignore cleanup errors
    }
  }
}

export function loadSettings() {
  const saved = readJsonSafe(SETTINGS_PATH, {});
  const merged = {
    ...DEFAULT_SETTINGS,
    ...(saved && typeof saved === "object" ? saved : {}),
  };
  if (!Array.isArray(merged.customLines) || merged.customLines.length === 0) {
    merged.customLines = DEFAULT_CUSTOM_LINES;
  }
  if (!Array.isArray(merged.clickSequence) || merged.clickSequence.length === 0) {
    merged.clickSequence = ["quota", "quote", "stats"];
  }
  return merged;
}

export function saveSettings(patch) {
  const current = loadSettings();
  const next = {
    ...current,
    ...(patch && typeof patch === "object" ? patch : {}),
  };
  if (typeof next.scale === "number") {
    next.scale = Math.max(0.6, Math.min(1.6, Number(next.scale.toFixed(2))));
  }
  if (typeof next.soundVolume === "number") {
    next.soundVolume = Math.max(0, Math.min(1, Number(next.soundVolume.toFixed(2))));
  }
  if (typeof next.lowQuotaAlertPct === "number") {
    next.lowQuotaAlertPct = Math.max(1, Math.min(95, Math.round(next.lowQuotaAlertPct)));
  }
  writeJsonAtomic(SETTINGS_PATH, next);
  return next;
}

export function loadState() {
  const saved = readJsonSafe(STATE_PATH, null);
  const base = createEmptyState();
  if (!saved || typeof saved !== "object") return base;
  return {
    ...base,
    ...saved,
    quotas: saved.quotas && typeof saved.quotas === "object" ? saved.quotas : {},
    stats: {
      ...base.stats,
      ...(saved.stats && typeof saved.stats === "object" ? saved.stats : {}),
      daily:
        saved.stats && saved.stats.daily && typeof saved.stats.daily === "object"
          ? saved.stats.daily
          : {},
    },
  };
}

export function saveState(state) {
  state.updatedAt = new Date().toISOString();
  pruneDailyStats(state, 90);
  writeJsonAtomic(STATE_PATH, state);
  return state;
}

export function todayKey(date = new Date()) {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, "0");
  const d = String(date.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

function pruneDailyStats(state, keepDays = 90) {
  const daily = state.stats?.daily;
  if (!daily) return;
  const keys = Object.keys(daily).sort();
  if (keys.length <= keepDays) return;
  const toDrop = keys.slice(0, keys.length - keepDays);
  for (const k of toDrop) delete daily[k];
}

export function friendlyBucketLabel(bucketId) {
  const id = String(bucketId || "").toLowerCase();
  if (id === "gemini-5h") return "Gemini 5小时额度";
  if (id === "gemini-weekly") return "Gemini 周额度";
  if (id.includes("argon")) return "Gemini Argon 周额度";
  if (id === "3p-5h") return "第三方模型 5小时额度";
  if (id === "3p-weekly") return "第三方模型 周额度";
  if (id.includes("gemini") && id.includes("week")) return `Gemini 周额度 (${bucketId})`;
  if (id.includes("gemini") && (id.includes("5h") || id.includes("hour") || id.includes("short"))) {
    return "Gemini 5小时额度";
  }
  if (id.includes("gemini") && id.includes("pro")) return "Gemini Pro 额度";
  if (id.includes("gemini") && id.includes("flash")) return "Gemini Flash 额度";
  if (id.includes("gemini")) return `Gemini (${bucketId})`;
  return bucketId || "Gemini 额度";
}

export function normalizeQuotaBucket(bucketId, raw, source = "agy-statusline", nowIso = new Date().toISOString()) {
  if (!raw || typeof raw !== "object") return null;
  let frac = null;
  if (typeof raw.remaining_fraction === "number" && Number.isFinite(raw.remaining_fraction)) {
    frac = Math.max(0, Math.min(1, raw.remaining_fraction));
  } else if (typeof raw.remainingFraction === "number" && Number.isFinite(raw.remainingFraction)) {
    frac = Math.max(0, Math.min(1, raw.remainingFraction));
  } else if (typeof raw.remainingPercent === "number" && Number.isFinite(raw.remainingPercent)) {
    frac = Math.max(0, Math.min(1, raw.remainingPercent / 100));
  }

  let resetTime = raw.reset_time || raw.resetTime || null;
  let resetInSeconds =
    typeof raw.reset_in_seconds === "number" && Number.isFinite(raw.reset_in_seconds)
      ? Math.max(0, Math.round(raw.reset_in_seconds))
      : typeof raw.resetInSeconds === "number" && Number.isFinite(raw.resetInSeconds)
        ? Math.max(0, Math.round(raw.resetInSeconds))
        : null;

  if (!resetTime && resetInSeconds !== null) {
    resetTime = new Date(Date.now() + resetInSeconds * 1000).toISOString();
  }

  return {
    id: bucketId,
    label: raw.label || friendlyBucketLabel(bucketId),
    remainingFraction: frac,
    remainingPercent: frac !== null ? Number((frac * 100).toFixed(1)) : null,
    remainingAmount:
      typeof raw.remaining_amount === "number"
        ? raw.remaining_amount
        : typeof raw.remainingAmount === "number"
          ? raw.remainingAmount
          : null,
    resetTime,
    resetInSeconds,
    disabled: Boolean(raw.disabled),
    source,
    updatedAt: nowIso,
  };
}

export function getLiveResetSeconds(bucket, nowMs = Date.now()) {
  if (!bucket) return null;
  if (bucket.resetTime) {
    const target = Date.parse(bucket.resetTime);
    if (Number.isFinite(target)) {
      return Math.max(0, Math.round((target - nowMs) / 1000));
    }
  }
  if (typeof bucket.resetInSeconds === "number") {
    const updated = bucket.updatedAt ? Date.parse(bucket.updatedAt) : nowMs;
    const elapsed = Number.isFinite(updated) ? Math.max(0, Math.round((nowMs - updated) / 1000)) : 0;
    return Math.max(0, bucket.resetInSeconds - elapsed);
  }
  return null;
}

export function formatCountdown(seconds) {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return "未知";
  if (seconds <= 0) return "已到重置时间";
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  if (d > 0) return `${d}天 ${h}小时`;
  if (h > 0) return `${h}小时 ${m}分`;
  return `${Math.max(1, m)}分钟`;
}

export function formatResetPhrase(seconds) {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return "未设置重置时间";
  if (seconds <= 0) return "已过重置时间 · 点击刷新";
  return `${formatCountdown(seconds)} 后重置`;
}

export function pickPrimaryGeminiBucket(state, preferredBucketId = "") {
  const buckets = Object.values(state.quotas || {}).filter((b) => b && !b.disabled && b.remainingPercent !== null);
  if (buckets.length === 0) return null;
  if (preferredBucketId && state.quotas[preferredBucketId] && !state.quotas[preferredBucketId].disabled) {
    return state.quotas[preferredBucketId];
  }
  const geminiBuckets = buckets.filter((b) => String(b.id).toLowerCase().includes("gemini"));
  const pool = geminiBuckets.length > 0 ? geminiBuckets : buckets;
  // Prefer agy-statusline live data over manual calibration if both exist
  const live = pool.filter((b) => b.source === "agy-statusline");
  const candidates = live.length > 0 ? live : pool;
  // Prefer shortest window (e.g. 5h) or weekly if named gemini-weekly
  const fiveH = candidates.find((b) => /5h|hour|short/i.test(b.id));
  if (fiveH) return fiveH;
  const weekly = candidates.find((b) => /week/i.test(b.id));
  if (weekly) return weekly;
  return candidates[0];
}

export function getSnapshot(settingsOverride = null) {
  const state = loadState();
  const settings = settingsOverride || loadSettings();
  const nowMs = Date.now();

  const buckets = Object.values(state.quotas || {})
    .filter(Boolean)
    .map((b) => {
      const liveSecs = getLiveResetSeconds(b, nowMs);
      return {
        ...b,
        label: friendlyBucketLabel(b.id),
        liveResetSeconds: liveSecs,
        resetCountdownText: formatCountdown(liveSecs),
        resetPhrase: formatResetPhrase(liveSecs),
      };
    })
    .sort((a, b) => {
      const aGem = String(a.id).toLowerCase().includes("gemini") ? 0 : 1;
      const bGem = String(b.id).toLowerCase().includes("gemini") ? 0 : 1;
      if (aGem !== bGem) return aGem - bGem;
      return String(a.id).localeCompare(String(b.id));
    });

  const primary = pickPrimaryGeminiBucket(state, settings.preferredBucketId);
  const primaryEnriched = primary
    ? buckets.find((b) => b.id === primary.id) || primary
    : null;

  const today = todayKey();
  const todayStats = state.stats?.daily?.[today] || { turns: 0, models: {}, sessions: [] };

  // Build 7-day rolling summary
  const last7Days = [];
  for (let i = 6; i >= 0; i--) {
    const dt = new Date(nowMs - i * 86400 * 1000);
    const key = todayKey(dt);
    const entry = state.stats?.daily?.[key];
    last7Days.push({
      date: key,
      label: key.slice(5),
      turns: entry?.turns || 0,
    });
  }

  return {
    runtimeDir: RUNTIME_DIR,
    statePath: STATE_PATH,
    settingsPath: SETTINGS_PATH,
    state,
    settings,
    primaryQuota: primaryEnriched,
    buckets,
    todayStats: {
      date: today,
      turns: todayStats.turns || 0,
      models: todayStats.models || {},
      sessionCount: Array.isArray(todayStats.sessions) ? todayStats.sessions.length : 0,
    },
    last7Days,
  };
}

export function applyStatusLinePayload(payload) {
  const state = loadState();
  const nowIso = new Date().toISOString();

  if (payload.plan_tier) state.planTier = payload.plan_tier;
  if (payload.email) state.email = payload.email;
  if (payload.agent_state) state.agentState = payload.agent_state;
  if (payload.conversation_id) state.activeConversationId = payload.conversation_id;
  if (payload.conversation_title) state.activeConversationTitle = payload.conversation_title;

  if (payload.model && typeof payload.model === "object") {
    state.activeModel = payload.model.display_name || payload.model.id || state.activeModel;
  } else if (typeof payload.model === "string") {
    state.activeModel = payload.model;
  }

  if (payload.context_window && typeof payload.context_window === "object") {
    state.contextWindow = {
      usedPercentage: payload.context_window.used_percentage ?? null,
      remainingPercentage: payload.context_window.remaining_percentage ?? null,
      totalInputTokens: payload.context_window.total_input_tokens ?? null,
      totalOutputTokens: payload.context_window.total_output_tokens ?? null,
      contextWindowSize: payload.context_window.context_window_size ?? null,
    };
  }

  if (payload.cost && typeof payload.cost === "object") {
    state.cost = {
      totalUsd: payload.cost.total_usd ?? null,
      subagentUsd: payload.cost.subagent_usd ?? null,
      estimated: Boolean(payload.cost.estimated),
    };
  }

  if (payload.quota && typeof payload.quota === "object") {
    const incomingIds = new Set(Object.keys(payload.quota));
    // Drop stale agy-statusline buckets that are no longer reported by agy
    for (const [existingId, existingVal] of Object.entries(state.quotas)) {
      if (existingVal?.source === "agy-statusline" && !incomingIds.has(existingId)) {
        delete state.quotas[existingId];
      }
    }
    for (const [bucketId, raw] of Object.entries(payload.quota)) {
      const normalized = normalizeQuotaBucket(bucketId, raw, "agy-statusline", nowIso);
      if (normalized) {
        state.quotas[bucketId] = normalized;
      }
    }
    state.source = "agy-statusline";
  }

  return saveState(state);
}

export function applyManualCalibration({ bucketId = "gemini-weekly", label, remainingPercent, resetInHours = null, resetInMinutes = null }) {
  const state = loadState();
  const nowIso = new Date().toISOString();
  const pct = Math.max(0, Math.min(100, Number(remainingPercent)));
  if (!Number.isFinite(pct)) {
    throw new Error("remainingPercent must be a finite number between 0 and 100");
  }

  let resetInSeconds = null;
  if (resetInHours !== null || resetInMinutes !== null) {
    const h = Number(resetInHours) || 0;
    const m = Number(resetInMinutes) || 0;
    const total = Math.round(h * 3600 + m * 60);
    if (total > 0) resetInSeconds = total;
  }
  const resetTime = resetInSeconds !== null ? new Date(Date.now() + resetInSeconds * 1000).toISOString() : null;

  const cleanId = String(bucketId || "gemini-weekly").trim() || "gemini-weekly";
  const bucket = normalizeQuotaBucket(
    cleanId,
    {
      label: label || friendlyBucketLabel(cleanId),
      remainingPercent: pct,
      resetTime,
      resetInSeconds,
    },
    "manual",
    nowIso
  );

  state.quotas[cleanId] = bucket;
  state.manualCalibration = {
    bucketId: cleanId,
    label: bucket.label,
    remainingPercent: bucket.remainingPercent,
    resetAtIso: resetTime,
    updatedAt: nowIso,
  };
  if (state.source === "none" || state.source === "hooks-only") {
    state.source = "manual";
  }
  return saveState(state);
}

export function applyHookEvent(eventType, payload = {}) {
  const state = loadState();
  const nowIso = new Date().toISOString();
  const today = todayKey();

  if (!state.stats.daily[today]) {
    state.stats.daily[today] = { turns: 0, models: {}, sessions: [] };
  }
  const day = state.stats.daily[today];

  const modelName = payload.modelName || payload.model?.display_name || payload.model?.id || null;
  const convId = payload.conversationId || payload.conversation_id || null;

  if (modelName) state.activeModel = modelName;
  if (convId) {
    state.activeConversationId = convId;
    if (!day.sessions.includes(convId)) {
      day.sessions.push(convId);
      if (day.sessions.length > 200) day.sessions = day.sessions.slice(-200);
    }
  }

  if (eventType === "PostInvocation") {
    day.turns = (day.turns || 0) + 1;
    state.stats.totalTurns = (state.stats.totalTurns || 0) + 1;
    state.stats.lastTurnAt = nowIso;
    if (payload.terminationReason) {
      state.stats.lastTerminationReason = payload.terminationReason;
    }
    const key = modelName || "Unknown Model";
    day.models[key] = (day.models[key] || 0) + 1;
  }

  if (state.source === "none") {
    state.source = "hooks-only";
  }
  return saveState(state);
}

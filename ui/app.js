import {
  App,
  applyDocumentTheme,
  applyHostFonts,
  applyHostStyleVariables,
} from "@modelcontextprotocol/ext-apps";

const app = new App({ name: "GeminiQuotaCatgirl", version: "1.0.0" });

const state = {
  snapshot: null,
  clickIndex: 0,
  lastQuote: "",
  activeDrawer: null, // "calibrate" | "settings" | null
  audioCtx: null,
  bubbleTimer: null,
};

const $ = (id) => document.getElementById(id);

function ensureAudioCtx() {
  if (!state.audioCtx) {
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (Ctx) state.audioCtx = new Ctx();
  }
  if (state.audioCtx && state.audioCtx.state === "suspended") {
    state.audioCtx.resume().catch(() => {});
  }
  return state.audioCtx;
}

function playSynthPop(kind) {
  const cfg = state.snapshot?.settings;
  if (cfg && cfg.soundEnabled === false) return;
  const vol = Math.max(0, Math.min(1, cfg?.soundVolume ?? 0.7)) * 0.25;
  if (vol <= 0.001) return;
  try {
    const ctx = ensureAudioCtx();
    if (!ctx) return;
    const now = ctx.currentTime;
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();
    osc.type = "sine";
    if (kind === "down") {
      osc.frequency.setValueAtTime(520, now);
      osc.frequency.exponentialRampToValueAtTime(320, now + 0.085);
      gain.gain.setValueAtTime(0.001, now);
      gain.gain.linearRampToValueAtTime(vol, now + 0.015);
      gain.gain.exponentialRampToValueAtTime(0.001, now + 0.095);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start(now);
      osc.stop(now + 0.1);
    } else {
      osc.frequency.setValueAtTime(360, now);
      osc.frequency.exponentialRampToValueAtTime(720, now + 0.11);
      gain.gain.setValueAtTime(0.001, now);
      gain.gain.linearRampToValueAtTime(vol, now + 0.014);
      gain.gain.exponentialRampToValueAtTime(0.001, now + 0.13);
      osc.connect(gain);
      gain.connect(ctx.destination);
      osc.start(now);
      osc.stop(now + 0.135);
    }
  } catch {
    // ignore audio autoplay issues
  }
}

function formatCountdown(seconds) {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return "未知";
  if (seconds <= 0) return "已到重置时间";
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (d > 0) return `${d}天 ${h}时`;
  if (h > 0) return `${h}时 ${m}分`;
  if (m > 0) return `${m}分 ${s}秒`;
  return `${s}秒`;
}

function formatResetPhrase(seconds) {
  if (seconds === null || seconds === undefined || !Number.isFinite(seconds)) return "未设置重置时间";
  if (seconds <= 0) return "已过重置时间 · 点刷新同步";
  return `${formatCountdown(seconds)} 后重置`;
}

function getLiveResetSecs(bucket) {
  if (!bucket) return null;
  if (bucket.resetTime) {
    const t = Date.parse(bucket.resetTime);
    if (Number.isFinite(t)) return Math.max(0, Math.round((t - Date.now()) / 1000));
  }
  if (typeof bucket.liveResetSeconds === "number") return bucket.liveResetSeconds;
  return null;
}

function quotaTone(pct, alertPct = 20) {
  if (pct === null || pct === undefined) return "idle";
  if (pct <= alertPct) return "low";
  if (pct <= 50) return "mid";
  return "high";
}

function pickWeightedQuote() {
  const snap = state.snapshot;
  const lines = snap?.settings?.customLines || [];
  const pct = snap?.primaryQuota?.remainingPercent ?? null;
  const alertPct = snap?.settings?.lowQuotaAlertPct ?? 20;
  const mood = quotaTone(pct, alertPct);

  const eligible = lines.filter((l) => !l.mood || l.mood === "any" || l.mood === mood);
  const pool = eligible.length > 0 ? eligible : lines;
  const nonRepeat = pool.filter((l) => l.text !== state.lastQuote);
  const finalPool = nonRepeat.length > 0 ? nonRepeat : pool;
  if (finalPool.length === 0) return "谷谷歌歌…随时待命中喵！";

  const totalWeight = finalPool.reduce((acc, item) => acc + Math.max(1, Number(item.weight) || 10), 0);
  let r = Math.random() * totalWeight;
  for (const item of finalPool) {
    r -= Math.max(1, Number(item.weight) || 10);
    if (r <= 0) {
      state.lastQuote = item.text;
      return item.text;
    }
  }
  state.lastQuote = finalPool[0].text;
  return finalPool[0].text;
}

function renderBubble(modeOverride = null) {
  const snap = state.snapshot;
  const seq = snap?.settings?.clickSequence?.length ? snap.settings.clickSequence : ["quota", "quote", "stats"];
  const mode = modeOverride || seq[state.clickIndex % seq.length];
  const bubbleEl = $("speechBubble");
  const titleEl = $("bubbleTag");
  const mainEl = $("bubbleMain");
  const subEl = $("bubbleSub");
  const barWrap = $("bubbleBarWrap");
  const barFill = $("bubbleBarFill");

  bubbleEl.classList.remove("pop-in");
  void bubbleEl.offsetWidth;
  bubbleEl.classList.add("pop-in");

  const primary = snap?.primaryQuota;
  const alertPct = snap?.settings?.lowQuotaAlertPct ?? 20;

  if (mode === "quota") {
    if (primary && primary.remainingPercent !== null) {
      const pct = primary.remainingPercent;
      const tone = quotaTone(pct, alertPct);
      titleEl.textContent = primary.label || "Gemini 剩余额度";
      mainEl.innerHTML = `<span class="pct-big tone-${tone}">${pct.toFixed(1)}<small>%</small></span>`;
      const secs = getLiveResetSecs(primary);
      const srcTag = primary.source === "agy-statusline" ? "agy 实时" : "手动校准";
      subEl.textContent = `${srcTag} · ${secs !== null ? formatResetPhrase(secs) : "点猫娘切换台词"}`;
      barWrap.hidden = false;
      barFill.style.width = `${Math.max(2, Math.min(100, pct))}%`;
      barFill.className = `bubble-bar-fill tone-${tone}`;
    } else {
      titleEl.textContent = "Gemini 额度猫娘";
      mainEl.innerHTML = `<span class="quote-text">谷谷歌歌…还没收到额度数据喵</span>`;
      subEl.textContent = "点击下方「校准额度」或接入 agy statusLine";
      barWrap.hidden = true;
    }
  } else if (mode === "quote") {
    titleEl.textContent = "猫娘悄悄话";
    mainEl.innerHTML = `<span class="quote-text">${escapeHtml(pickWeightedQuote())}</span>`;
    const model = snap?.state?.activeModel || "Gemini";
    subEl.textContent = `当前模型：${model}`;
    barWrap.hidden = true;
  } else {
    const today = snap?.todayStats || { turns: 0, sessionCount: 0 };
    const total = snap?.state?.stats?.totalTurns || 0;
    titleEl.textContent = "今日反重力统计";
    mainEl.innerHTML = `<span class="pct-big tone-high">${today.turns}<small> 轮</small></span>`;
    subEl.textContent = `今日 ${today.sessionCount} 个会话 · 累计 ${total} 轮调用`;
    barWrap.hidden = true;
  }
}

function escapeHtml(str) {
  return String(str ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function renderQuotaCards() {
  const snap = state.snapshot;
  const container = $("quotaList");
  const buckets = snap?.buckets || [];
  const alertPct = snap?.settings?.lowQuotaAlertPct ?? 20;

  if (buckets.length === 0) {
    container.innerHTML = `
      <div class="empty-card">
        <div class="empty-title">尚未检测到 Gemini 实时额度</div>
        <div class="empty-desc">
          Antigravity Desktop 目前通过 <code>hooks.json</code> 自动统计模型与对话轮次；
          额度百分比可通过 <b>手动校准</b> 或连接官方 <code>agy</code> CLI <code>statusLine</code> 自动实时同步。
        </div>
      </div>`;
    return;
  }

  container.innerHTML = buckets
    .map((b) => {
      const pct = b.remainingPercent ?? 0;
      const tone = quotaTone(b.remainingPercent, alertPct);
      const secs = getLiveResetSecs(b);
      const resetText = formatResetPhrase(secs);
      const srcBadge = b.source === "agy-statusline" ? "agy 实时" : "手动校准";
      return `
        <div class="quota-card">
          <div class="quota-head">
            <span class="quota-label">${escapeHtml(b.label || b.id)}</span>
            <span class="badge badge-${b.source === "agy-statusline" ? "live" : "manual"}">${srcBadge}</span>
          </div>
          <div class="quota-row">
            <span class="quota-val tone-${tone}">${b.remainingPercent !== null ? `${pct.toFixed(1)}%` : "--"}</span>
            <span class="quota-reset" data-bucket-id="${escapeHtml(b.id)}">${escapeHtml(resetText)}</span>
          </div>
          <div class="progress-track">
            <div class="progress-fill tone-${tone}" style="width:${Math.max(2, Math.min(100, pct))}%"></div>
          </div>
        </div>`;
    })
    .join("");
}

function renderStats() {
  const snap = state.snapshot;
  const today = snap?.todayStats || { turns: 0, models: {}, sessionCount: 0 };
  const activeModel = snap?.state?.activeModel || "等待调用…";
  const planTier = snap?.state?.planTier ? ` · ${snap.state.planTier}` : "";

  $("activeModelChip").textContent = `${activeModel}${planTier}`;
  $("todayTurnsVal").textContent = String(today.turns);
  $("todaySessionsVal").textContent = String(today.sessionCount);
  $("totalTurnsVal").textContent = String(snap?.state?.stats?.totalTurns || 0);

  // Per-model breakdown
  const modelEntries = Object.entries(today.models || {}).sort((a, b) => b[1] - a[1]);
  const modelWrap = $("modelBreakdown");
  if (modelEntries.length === 0) {
    modelWrap.innerHTML = `<div class="muted-note">今天还没有通过 Hook 记录的模型调用。</div>`;
  } else {
    const maxCnt = Math.max(1, ...modelEntries.map(([, c]) => c));
    modelWrap.innerHTML = modelEntries
      .slice(0, 5)
      .map(
        ([name, cnt]) => `
        <div class="model-row">
          <span class="model-name" title="${escapeHtml(name)}">${escapeHtml(name)}</span>
          <div class="model-bar"><div class="model-bar-fill" style="width:${Math.round((cnt / maxCnt) * 100)}%"></div></div>
          <span class="model-cnt">${cnt}</span>
        </div>`
      )
      .join("");
  }

  // 7-day mini bars
  const days = snap?.last7Days || [];
  const maxDay = Math.max(1, ...days.map((d) => d.turns));
  $("spark7d").innerHTML = days
    .map((d) => {
      const hPct = Math.max(8, Math.round((d.turns / maxDay) * 100));
      return `
        <div class="spark-col" title="${d.date}: ${d.turns} 轮">
          <div class="spark-bar-wrap"><div class="spark-bar" style="height:${hPct}%"></div></div>
          <span class="spark-lbl">${d.label}</span>
        </div>`;
    })
    .join("");
}

function syncSettingsForm() {
  const s = state.snapshot?.settings;
  if (!s) return;
  $("setPreferredBucket").value = s.preferredBucketId || "";
  $("setAutoRefresh").checked = s.autoRefreshEnabled !== false;
  $("setRefreshMins").value = String(Math.max(1, Math.min(60, Number(s.autoRefreshMinutes) || 5)));
  $("setRefreshAfterTurn").checked = s.refreshAfterTurn !== false;
  $("setOnlyInAgy").checked = s.onlyShowInAntigravity !== false;
  $("setAutoLaunch").checked = s.autoLaunchWithAntigravity !== false;
  $("setAutoClose").checked = Boolean(s.autoCloseWithAntigravity);
  $("setSoundEnabled").checked = Boolean(s.soundEnabled);
  $("setSoundVolume").value = String(Math.round((s.soundVolume ?? 0.7) * 100));
  $("setSoundVolLabel").textContent = `${Math.round((s.soundVolume ?? 0.7) * 100)}%`;
  $("setLowAlert").value = String(s.lowQuotaAlertPct ?? 20);
  $("setCustomLines").value = (s.customLines || [])
    .map((l) => `${l.text} | ${l.weight ?? 20} | ${l.mood || "any"}`)
    .join("\n");
  if (state.snapshot?.agySetupScriptPath) {
    $("agySetupCmd").textContent = `powershell -ExecutionPolicy Bypass -File "${state.snapshot.agySetupScriptPath}"`;
  }
}

function renderAll(preserveBubble = false) {
  if (!state.snapshot) return;
  renderQuotaCards();
  renderStats();
  syncSettingsForm();
  if (!preserveBubble) renderBubble();
  updateStatusFooter();
}

function updateStatusFooter() {
  const updated = state.snapshot?.state?.updatedAt;
  const txt = updated ? `最后同步：${new Date(updated).toLocaleTimeString()}` : "等待同步";
  $("syncTimeLabel").textContent = txt;
}

function setToast(msg, isErr = false) {
  const el = $("toast");
  el.textContent = msg;
  el.className = `toast show ${isErr ? "err" : ""}`;
  clearTimeout(setToast._t);
  setToast._t = setTimeout(() => {
    el.className = "toast";
  }, 2600);
}

function toggleDrawer(name) {
  state.activeDrawer = state.activeDrawer === name ? null : name;
  $("calibrateDrawer").hidden = state.activeDrawer !== "calibrate";
  $("settingsDrawer").hidden = state.activeDrawer !== "settings";
  $("btnCalibrate").classList.toggle("active", state.activeDrawer === "calibrate");
  $("btnSettings").classList.toggle("active", state.activeDrawer === "settings");
}

function extractSnapshot(toolResult) {
  if (!toolResult) return null;
  if (toolResult.structuredContent && toolResult.structuredContent.state) {
    return toolResult.structuredContent;
  }
  const textPart = toolResult.content?.find((c) => c.type === "text")?.text;
  if (textPart) {
    try {
      const parsed = JSON.parse(textPart);
      if (parsed && parsed.state) return parsed;
    } catch {
      // plain text fallback
    }
  }
  return null;
}

async function refreshSnapshot(quiet = false) {
  try {
    const res = await app.callServerTool({ name: "get_wallet_state", arguments: {} });
    const snap = extractSnapshot(res);
    if (snap) {
      state.snapshot = snap;
      renderAll(quiet);
      if (!quiet) setToast("已刷新 Gemini 额度状态 ✨");
    }
  } catch (err) {
    if (!quiet) setToast(`刷新失败：${err.message || err}`, true);
  }
}

async function syncAgyLive() {
  const btn = $("btnRefresh");
  btn.disabled = true;
  btn.textContent = "⏳ 同步中";
  try {
    const res = await app.callServerTool({ name: "sync_agy_quota", arguments: {} });
    const snap = extractSnapshot(res);
    if (snap) {
      state.snapshot = snap;
      renderAll(false);
      if (snap.syncResult && snap.syncResult.ok === false) {
        setToast("本地已刷新（agy 同步失败，请检查代理）", true);
      } else {
        setToast("已从 agy 同步最新真实 Gemini 额度 ✨");
      }
    }
  } catch (err) {
    await refreshSnapshot(true);
    setToast(`同步失败：${err.message || err}`, true);
  } finally {
    btn.disabled = false;
    btn.textContent = "🔄 刷新";
  }
}

function bindEvents() {
  const mascotBtn = $("mascotStage");
  const mascotImg = $("mascotImg");

  mascotBtn.addEventListener("pointerdown", () => {
    mascotImg.classList.remove("bouncing");
    mascotImg.classList.add("squished");
    playSynthPop("down");
  });

  const releaseMascot = (doCycle) => {
    mascotImg.classList.remove("squished");
    mascotImg.classList.add("bouncing");
    setTimeout(() => mascotImg.classList.remove("bouncing"), 320);
    if (doCycle) {
      playSynthPop("up");
      state.clickIndex += 1;
      renderBubble();
    }
  };

  mascotBtn.addEventListener("pointerup", () => releaseMascot(true));
  mascotBtn.addEventListener("pointerleave", () => {
    if (mascotImg.classList.contains("squished")) releaseMascot(false);
  });

  $("btnRefresh").addEventListener("click", () => syncAgyLive());
  $("btnCalibrate").addEventListener("click", () => toggleDrawer("calibrate"));
  $("btnSettings").addEventListener("click", () => toggleDrawer("settings"));

  $("btnLaunchDesktop").addEventListener("click", async () => {
    try {
      const res = await app.callServerTool({ name: "launch_desktop_widget", arguments: {} });
      const msg = res?.content?.find((c) => c.type === "text")?.text || "已启动桌面悬浮猫娘挂件！";
      setToast(msg);
    } catch (err) {
      setToast(`启动失败：${err.message || err}`, true);
    }
  });

  $("calPctRange").addEventListener("input", (e) => {
    $("calPctInput").value = e.target.value;
  });
  $("calPctInput").addEventListener("input", (e) => {
    const v = Math.max(0, Math.min(100, Number(e.target.value) || 0));
    $("calPctRange").value = String(v);
  });

  $("btnSaveCalibration").addEventListener("click", async () => {
    const bucketId = $("calBucket").value;
    const remainingPercent = Number($("calPctInput").value);
    const resetInHours = $("calHours").value === "" ? null : Number($("calHours").value);
    const resetInMinutes = $("calMinutes").value === "" ? null : Number($("calMinutes").value);
    try {
      const res = await app.callServerTool({
        name: "calibrate_quota",
        arguments: { bucketId, remainingPercent, resetInHours, resetInMinutes },
      });
      const snap = extractSnapshot(res);
      if (snap) {
        state.snapshot = snap;
        state.clickIndex = 0;
        renderAll(false);
        toggleDrawer(null);
        setToast("Gemini 额度已校准并同步到桌面挂件 ✨");
      }
    } catch (err) {
      setToast(`校准失败：${err.message || err}`, true);
    }
  });

  $("setSoundVolume").addEventListener("input", (e) => {
    $("setSoundVolLabel").textContent = `${e.target.value}%`;
  });

  $("btnSaveSettings").addEventListener("click", async () => {
    const rawLines = $("setCustomLines")
      .value.split(/\r?\n/)
      .map((l) => l.trim())
      .filter(Boolean);
    const customLines = rawLines.map((line) => {
      const parts = line.split("|").map((p) => p.trim());
      return {
        text: parts[0],
        weight: Math.max(1, Number(parts[1]) || 20),
        mood: ["any", "high", "mid", "low"].includes(parts[2]) ? parts[2] : "any",
      };
    });

    const patch = {
      preferredBucketId: $("setPreferredBucket").value || "",
      autoRefreshEnabled: $("setAutoRefresh").checked,
      autoRefreshMinutes: Math.max(1, Math.min(60, Number($("setRefreshMins").value) || 5)),
      refreshAfterTurn: $("setRefreshAfterTurn").checked,
      onlyShowInAntigravity: $("setOnlyInAgy").checked,
      autoLaunchWithAntigravity: $("setAutoLaunch").checked,
      autoCloseWithAntigravity: $("setAutoClose").checked,
      soundEnabled: $("setSoundEnabled").checked,
      soundVolume: Number($("setSoundVolume").value) / 100,
      lowQuotaAlertPct: Number($("setLowAlert").value) || 20,
      customLines: customLines.length ? customLines : undefined,
    };

    try {
      const res = await app.callServerTool({
        name: "save_wallet_settings",
        arguments: { patch },
      });
      const snap = extractSnapshot(res);
      if (snap) {
        state.snapshot = snap;
        renderAll(true);
        toggleDrawer(null);
        setToast("设置已保存并同步到桌面挂件！");
      }
    } catch (err) {
      setToast(`保存失败：${err.message || err}`, true);
    }
  });

  $("btnCopyAgyCmd").addEventListener("click", async () => {
    const cmd = $("agySetupCmd").textContent;
    try {
      await navigator.clipboard.writeText(cmd);
      setToast("已复制 agy statusLine 配置命令");
    } catch {
      setToast("请手动选中上方命令复制");
    }
  });
}

function applyHostContext(ctx) {
  if (!ctx) return;
  if (ctx.theme) applyDocumentTheme(ctx.theme);
  if (ctx.styles?.variables) applyHostStyleVariables(ctx.styles.variables);
  if (ctx.styles?.css?.fonts) applyHostFonts(ctx.styles.css.fonts);
}

app.onhostcontextchanged = (ctx) => applyHostContext(ctx);

app.ontoolresult = (result) => {
  const snap = extractSnapshot(result);
  if (snap) {
    state.snapshot = snap;
    renderAll(false);
  }
};

bindEvents();

// Tick live countdowns every 10s and poll state every 15s for external agy/hook updates
setInterval(() => {
  if (state.snapshot) renderQuotaCards();
}, 10000);

setInterval(() => {
  if (state.snapshot) refreshSnapshot(true);
}, 15000);

app
  .connect()
  .then(() => {
    applyHostContext(app.getHostContext());
    if (!state.snapshot) refreshSnapshot(true);
  })
  .catch(() => {
    // Allow standalone preview if opened outside MCP host
  });

#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  RESOURCE_MIME_TYPE,
  registerAppResource,
  registerAppTool,
} from "@modelcontextprotocol/ext-apps/server";
import { z } from "zod";
import {
  applyManualCalibration,
  getSnapshot,
  loadSettings,
  saveSettings,
} from "../shared/store.mjs";
import { syncAgyQuotaSilent } from "../scripts/sync-agy-quota.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, "..");
const uiHtmlPath = path.join(rootDir, "ui", "dist", "app.html");
const agySetupScriptPath = path.join(rootDir, "scripts", "setup-agy-statusline.ps1");

const DESKTOP_EXE_CANDIDATES = [
  process.env.AGY_GEMINI_WIDGET_EXE,
  path.join(rootDir, "desktop", "GeminiWalletWidget.exe"),
  path.join(rootDir, "desktop", "dist", "GeminiWalletWidget.exe"),
  path.join(rootDir, "desktop", "bin", "Release", "GeminiWalletWidget.exe"),
].filter(Boolean);

function findDesktopExe() {
  return DESKTOP_EXE_CANDIDATES.find((p) => fs.existsSync(p)) || null;
}

function spawnDesktopWidget(args = []) {
  const exePath = findDesktopExe();
  if (!exePath) return null;
  try {
    const child = spawn(exePath, args, {
      detached: true,
      stdio: "ignore",
      windowsHide: false,
    });
    child.unref();
    return exePath;
  } catch {
    return null;
  }
}

const RESOURCE_URI = "ui://gemini-wallet/app.html";

function buildFullSnapshot() {
  const snap = getSnapshot();
  return {
    ...snap,
    agySetupScriptPath,
    desktopExePath: DESKTOP_EXE_CANDIDATES.find((p) => fs.existsSync(p)) || null,
  };
}

function summarizeSnapshotText(snap) {
  const p = snap.primaryQuota;
  const model = snap.state?.activeModel || "未记录";
  const turns = snap.todayStats?.turns ?? 0;
  if (p && p.remainingPercent !== null) {
    const resetText = p.resetPhrase || `${p.resetCountdownText}后重置`;
    return `🐱 谷谷歌歌…当前 ${p.label} 剩余 ${p.remainingPercent}%（${resetText}）| 当前模型：${model} | 今日调用：${turns} 轮`;
  }
  return `🐱 谷谷歌歌…暂未收到 Gemini 实时额度数据（当前模型：${model}，今日调用：${turns} 轮）。可在面板内点击「刷新」同步 agy 或手动校准。`;
}

const server = new McpServer({
  name: "gemini-wallet-widget",
  version: "1.0.0",
});

registerAppResource(
  server,
  "Gemini Quota Catgirl UI",
  RESOURCE_URI,
  {
    mimeType: RESOURCE_MIME_TYPE,
    description: "Interactive Antigravity Gemini Quota Catgirl Wallet Widget UI",
  },
  async () => {
    const html = fs.existsSync(uiHtmlPath)
      ? fs.readFileSync(uiHtmlPath, "utf8")
      : "<!doctype html><html><body>UI bundle missing. Run npm run build:ui.</body></html>";
    return {
      contents: [
        {
          uri: RESOURCE_URI,
          mimeType: RESOURCE_MIME_TYPE,
          text: html,
          _meta: {
            ui: {
              prefersBorder: false,
            },
          },
        },
      ],
    };
  }
);

registerAppTool(
  server,
  "open_gemini_wallet",
  {
    title: "Gemini 额度猫娘挂件",
    description:
      "Open the interactive Gemini Quota Catgirl Wallet Widget showing Gemini remaining quota, reset countdown, Antigravity turn stats, manual calibration, and desktop floating widget launcher.",
    inputSchema: {
      focus: z
        .enum(["overview", "calibrate", "settings"])
        .optional()
        .describe("Optional initial section to focus"),
    },
    _meta: {
      ui: {
        resourceUri: RESOURCE_URI,
      },
      "com.google.antigravity/ui": {
        entrypoints: [
          { type: "aux_pane", title: "Gemini 额度猫娘" },
          { type: "global", title: "Gemini 额度猫娘" },
        ],
      },
    },
  },
  async () => {
    const cur = buildFullSnapshot();
    const primary = cur.primaryQuota;
    const staleMs = primary?.updatedAt ? Date.now() - Date.parse(primary.updatedAt) : Infinity;
    const expired = primary?.liveResetSeconds !== null && primary?.liveResetSeconds <= 0;
    if (cur.settings.autoRefreshEnabled !== false && (staleMs > 120000 || expired)) {
      await syncAgyQuotaSilent({ timeoutMs: 8000 });
    }
    const snap = buildFullSnapshot();
    return {
      content: [{ type: "text", text: summarizeSnapshotText(snap) }],
      structuredContent: snap,
    };
  }
);

registerAppTool(
  server,
  "sync_agy_quota",
  {
    title: "Sync Real Gemini Quota via agy",
    description: "Silently run `agy models` (zero tokens) to fetch fresh real Gemini quotas into state.json.",
    inputSchema: {},
    _meta: {
      ui: {
        visibility: ["app"],
      },
    },
  },
  async () => {
    const syncResult = await syncAgyQuotaSilent({ timeoutMs: 15000 });
    const snap = buildFullSnapshot();
    const payload = { ...snap, syncResult };
    return {
      content: [{ type: "text", text: JSON.stringify(payload) }],
      structuredContent: payload,
    };
  }
);

registerAppTool(
  server,
  "get_wallet_state",
  {
    title: "Get Gemini Wallet State",
    description: "Return live Gemini quota snapshot, Antigravity turn stats, and settings.",
    inputSchema: {},
    _meta: {
      ui: {
        visibility: ["app"],
      },
    },
  },
  async () => {
    const snap = buildFullSnapshot();
    return {
      content: [{ type: "text", text: JSON.stringify(snap) }],
      structuredContent: snap,
    };
  }
);

registerAppTool(
  server,
  "calibrate_quota",
  {
    title: "Calibrate Gemini Quota",
    description: "Manually set Gemini remaining quota percentage and optional reset countdown.",
    inputSchema: {
      bucketId: z.string().optional(),
      label: z.string().optional(),
      remainingPercent: z.number().min(0).max(100),
      resetInHours: z.number().min(0).max(336).nullable().optional(),
      resetInMinutes: z.number().min(0).max(59).nullable().optional(),
    },
    _meta: {
      ui: {
        visibility: ["app"],
      },
    },
  },
  async (args) => {
    applyManualCalibration(args);
    const snap = buildFullSnapshot();
    return {
      content: [{ type: "text", text: JSON.stringify(snap) }],
      structuredContent: snap,
    };
  }
);

registerAppTool(
  server,
  "save_wallet_settings",
  {
    title: "Save Wallet Settings",
    description: "Persist wallet widget settings (sound, alerts, custom catgirl lines, scale).",
    inputSchema: {
      patch: z.record(z.any()),
    },
    _meta: {
      ui: {
        visibility: ["app"],
      },
    },
  },
  async ({ patch }) => {
    saveSettings(patch || {});
    const snap = buildFullSnapshot();
    return {
      content: [{ type: "text", text: JSON.stringify(snap) }],
      structuredContent: snap,
    };
  }
);

registerAppTool(
  server,
  "launch_desktop_widget",
  {
    title: "Launch Windows Floating Desktop Widget",
    description: "Launch the transparent always-on-top Windows desktop Gemini catgirl widget.",
    inputSchema: {},
    _meta: {
      ui: {
        visibility: ["app"],
      },
    },
  },
  async () => {
    const launched = spawnDesktopWidget([]);
    if (!launched) {
      return {
        isError: true,
        content: [
          {
            type: "text",
            text: "未找到桌面悬浮挂件程序 GeminiWalletWidget.exe，请先完成桌面端构建。",
          },
        ],
      };
    }
    return {
      content: [{ type: "text", text: "已启动 / 唤醒 Windows 桌面悬浮猫娘挂件 ✨" }],
    };
  }
);

function maybeAutoLaunchOnAntigravityStart() {
  if (process.env.AGY_GEMINI_NO_AUTOLAUNCH === "1") return;
  try {
    const settings = loadSettings();
    if (settings.autoLaunchWithAntigravity !== false) {
      spawnDesktopWidget(["--auto"]);
    }
  } catch {
    // Never block MCP startup
  }
}

let shutdownSignaled = false;
function maybeAutoCloseOnAntigravityExit() {
  if (shutdownSignaled || process.env.AGY_GEMINI_NO_AUTOLAUNCH === "1") return;
  shutdownSignaled = true;
  try {
    const settings = loadSettings();
    if (settings.autoCloseWithAntigravity === true) {
      spawnDesktopWidget(["--quit"]);
    }
  } catch {
    // Ignore shutdown errors
  }
}

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  maybeAutoLaunchOnAntigravityStart();
  process.stdin.once("end", maybeAutoCloseOnAntigravityExit);
  process.stdin.once("close", maybeAutoCloseOnAntigravityExit);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});

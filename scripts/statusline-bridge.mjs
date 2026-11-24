#!/usr/bin/env node
/**
 * Antigravity CLI (`agy`) statusLine bridge.
 * Reads official statusLine JSON from stdin (https://antigravity.google/docs/cli/statusline),
 * persists live Gemini quota / model / context window state to
 * ~/.gemini/antigravity-gemini-wallet/state.json for both the Windows desktop widget
 * and the Antigravity MCP App side pane, and prints a compact status line to stdout.
 */
import { applyStatusLinePayload, formatCountdown, getLiveResetSeconds, loadSettings, pickPrimaryGeminiBucket } from "../shared/store.mjs";

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString("utf8").trim();
}

async function main() {
  const raw = await readStdin();
  if (!raw) {
    process.stdout.write("🐱 Gemini 额度猫娘待命中");
    return;
  }

  let payload;
  try {
    payload = JSON.parse(raw);
  } catch {
    process.stdout.write("🐱 Gemini 额度猫娘");
    return;
  }

  const state = applyStatusLinePayload(payload);
  const settings = loadSettings();
  const primary = pickPrimaryGeminiBucket(state, settings.preferredBucketId);
  const modelName = state.activeModel || "Gemini";

  if (primary && primary.remainingPercent !== null) {
    const secs = getLiveResetSeconds(primary);
    const resetText = secs !== null ? ` · ${formatCountdown(secs)}重置` : "";
    process.stdout.write(`🐱 ${modelName} | ${primary.label} ${primary.remainingPercent}%${resetText}`);
  } else {
    process.stdout.write(`🐱 ${modelName} | 谷谷歌歌…`);
  }
}

main().catch(() => {
  process.stdout.write("🐱 Gemini 额度猫娘");
  process.exit(0);
});

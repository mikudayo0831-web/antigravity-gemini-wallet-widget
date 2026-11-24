#!/usr/bin/env node
/**
 * Antigravity Hooks (`hooks.json`) recorder.
 * Handles SessionStart / PostInvocation payloads on stdin (https://antigravity.google/docs/hooks),
 * updates ~/.gemini/antigravity-gemini-wallet/state.json with active model and daily turn stats,
 * and always outputs `{}` with exit code 0 so Antigravity is never blocked.
 */
import { applyHookEvent } from "../shared/store.mjs";

const eventType = process.argv[2] || "PostInvocation";

async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString("utf8").trim();
}

async function main() {
  const raw = await readStdin();
  let payload = {};
  if (raw) {
    try {
      payload = JSON.parse(raw);
    } catch {
      payload = {};
    }
  }
  applyHookEvent(eventType, payload);
  process.stdout.write("{}\n");
}

main().catch(() => {
  process.stdout.write("{}\n");
  process.exit(0);
});

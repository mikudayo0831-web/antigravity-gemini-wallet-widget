#!/usr/bin/env node
/**
 * Silently invokes official `agy models` (zero model tokens consumed) to trigger
 * Antigravity CLI's configured `statusLine` bridge and refresh live Gemini quotas
 * in ~/.gemini/antigravity-gemini-wallet/state.json.
 * Automatically reads Windows System Proxy (HKCU Internet Settings) when HTTPS_PROXY is unset.
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFile, execFileSync } from "node:child_process";

export function findAgyBinary() {
  const candidates = [
    process.env.AGY_BIN_PATH,
    path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), "agy", "bin", "agy.exe"),
  ].filter(Boolean);
  for (const p of candidates) {
    if (fs.existsSync(p)) return p;
  }
  return "agy";
}

export function detectWindowsSystemProxy() {
  if (process.env.HTTPS_PROXY || process.env.https_proxy || process.env.HTTP_PROXY || process.env.http_proxy) {
    return null;
  }
  if (process.platform !== "win32") return null;
  try {
    const out = execFileSync(
      "reg.exe",
      ["query", "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings"],
      { encoding: "utf8", windowsHide: true, timeout: 2500 }
    );
    const enabled = /ProxyEnable\s+REG_DWORD\s+0x1/i.test(out);
    if (!enabled) return null;
    const m = out.match(/ProxyServer\s+REG_SZ\s+([^\r\n]+)/i);
    if (!m) return null;
    let raw = m[1].trim();
    if (!raw) return null;
    // Handle per-protocol format like "http=127.0.0.1:7890;https=127.0.0.1:7890"
    if (raw.includes("=")) {
      const httpsMatch = raw.match(/https=([^;]+)/i) || raw.match(/http=([^;]+)/i);
      if (httpsMatch) raw = httpsMatch[1].trim();
    }
    if (!/^https?:\/\//i.test(raw)) raw = `http://${raw}`;
    return raw;
  } catch {
    return null;
  }
}

let inFlightSync = null;

export function syncAgyQuotaSilent({ timeoutMs = 15000 } = {}) {
  if (inFlightSync) return inFlightSync;
  inFlightSync = new Promise((resolve) => {
    const agyBin = findAgyBinary();
    const env = { ...process.env };
    const sysProxy = detectWindowsSystemProxy();
    if (sysProxy) {
      env.HTTP_PROXY = env.HTTP_PROXY || sysProxy;
      env.HTTPS_PROXY = env.HTTPS_PROXY || sysProxy;
    }

    execFile(
      agyBin,
      ["models"],
      { env, timeout: timeoutMs, windowsHide: true },
      (err, stdout, stderr) => {
        inFlightSync = null;
        if (err) {
          resolve({
            ok: false,
            error: err.message || String(err),
            stderr: String(stderr || "").trim(),
            proxyUsed: sysProxy || env.HTTPS_PROXY || null,
          });
          return;
        }
        resolve({
          ok: true,
          proxyUsed: sysProxy || env.HTTPS_PROXY || null,
        });
      }
    );
  });
  return inFlightSync;
}

if (import.meta.url === `file:///${process.argv[1]?.replace(/\\/g, "/")}`) {
  syncAgyQuotaSilent().then((res) => {
    console.log(JSON.stringify(res));
    process.exit(res.ok ? 0 : 1);
  });
}

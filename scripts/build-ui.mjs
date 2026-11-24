import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, "..");
const serverDir = path.join(rootDir, "server");
const requireFromServer = createRequire(path.join(serverDir, "package.json"));
const esbuild = requireFromServer("esbuild");

async function build() {
  const result = await esbuild.build({
    entryPoints: [path.join(rootDir, "ui", "app.js")],
    bundle: true,
    format: "esm",
    minify: true,
    write: false,
    nodePaths: [path.join(serverDir, "node_modules")],
    target: ["es2022"],
  });

  const bundledJs = result.outputFiles[0].text;
  const mascotPath = path.join(rootDir, "assets", "mascot.png");
  const mascotDataUri = `data:image/png;base64,${fs.readFileSync(mascotPath).toString("base64")}`;

  const template = fs.readFileSync(path.join(rootDir, "ui", "template.html"), "utf8");
  const html = template
    .replace("__MASCOT_DATA_URI__", () => mascotDataUri)
    .replace("__BUNDLED_APP_JS__", () => bundledJs);

  const distDir = path.join(rootDir, "ui", "dist");
  fs.mkdirSync(distDir, { recursive: true });
  const outPath = path.join(distDir, "app.html");
  fs.writeFileSync(outPath, html, "utf8");
  console.log(`Built MCP App single-file HTML: ${outPath} (${(Buffer.byteLength(html) / 1024).toFixed(1)} KiB)`);
}

build().catch((err) => {
  console.error(err);
  process.exit(1);
});

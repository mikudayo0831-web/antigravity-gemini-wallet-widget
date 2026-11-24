<#
.SYNOPSIS
  Installs the Gemini Quota Catgirl Antigravity plugin into ~/.gemini/config/plugins/gemini-wallet-widget/
#>
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$targetDir = Join-Path $env:USERPROFILE ".gemini\config\plugins\gemini-wallet-widget"

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir "assets") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir "shared") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir "scripts") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir "ui\dist") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir "server") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $targetDir "desktop") | Out-Null

# Plugin manifests
Copy-Item (Join-Path $projectRoot "plugin\plugin.json") (Join-Path $targetDir "plugin.json") -Force
Copy-Item (Join-Path $projectRoot "plugin\mcp_config.json") (Join-Path $targetDir "mcp_config.json") -Force
Copy-Item (Join-Path $projectRoot "plugin\assets\logo.png") (Join-Path $targetDir "assets\logo.png") -Force

# Resolve ${PLUGIN_ROOT} inside installed hooks.json so hooks work reliably on Windows regardless of shell expansion
$hooksRaw = Get-Content (Join-Path $projectRoot "plugin\hooks.json") -Raw -Encoding UTF8
$pluginRootForward = ($targetDir -replace '\\', '/')
$hooksResolved = $hooksRaw.Replace('${PLUGIN_ROOT}', $pluginRootForward)
[System.IO.File]::WriteAllText((Join-Path $targetDir "hooks.json"), $hooksResolved, [System.Text.UTF8Encoding]::new($false))

# Shared state, hooks, statusLine scripts, single-file UI, and MCP server
Copy-Item (Join-Path $projectRoot "shared\store.mjs") (Join-Path $targetDir "shared\store.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\hook-recorder.mjs") (Join-Path $targetDir "scripts\hook-recorder.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\statusline-bridge.mjs") (Join-Path $targetDir "scripts\statusline-bridge.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\sync-agy-quota.mjs") (Join-Path $targetDir "scripts\sync-agy-quota.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\setup-agy-statusline.ps1") (Join-Path $targetDir "scripts\setup-agy-statusline.ps1") -Force
Copy-Item (Join-Path $projectRoot "ui\dist\app.html") (Join-Path $targetDir "ui\dist\app.html") -Force
Copy-Item (Join-Path $projectRoot "server\mcp-server.mjs") (Join-Path $targetDir "server\mcp-server.mjs") -Force
Copy-Item (Join-Path $projectRoot "server\package.json") (Join-Path $targetDir "server\package.json") -Force

# Bundle self-contained Windows desktop floating widget inside the plugin
$desktopBuildDir = Join-Path $projectRoot "desktop\dist"
if (-not (Test-Path $desktopBuildDir)) {
    $desktopBuildDir = Join-Path $projectRoot "desktop\bin\Release"
}
$dstDesktopDir = Join-Path $targetDir "desktop"
$existingExe = Join-Path $dstDesktopDir "GeminiWalletWidget.exe"
if (Test-Path $existingExe) {
    try { & $existingExe --quit | Out-Null; Start-Sleep -Milliseconds 400 } catch {}
}
if (Test-Path $desktopBuildDir) {
    Copy-Item -Path (Join-Path $desktopBuildDir "*") -Destination $dstDesktopDir -Recurse -Force
}

# Mirror runtime node_modules (excluding dev-only esbuild to keep plugin lightweight)
$srcNm = Join-Path $projectRoot "server\node_modules"
$dstNm = Join-Path $targetDir "server\node_modules"
if (Test-Path $dstNm) { Remove-Item $dstNm -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dstNm | Out-Null
Get-ChildItem -Path $srcNm -Force | Where-Object {
    $_.Name -notin @("esbuild", "@esbuild", ".bin")
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $dstNm $_.Name) -Recurse -Force
}

Write-Host "Plugin installed cleanly to: $targetDir"
Get-ChildItem -Path $targetDir | Select-Object Name, PSIsContainer

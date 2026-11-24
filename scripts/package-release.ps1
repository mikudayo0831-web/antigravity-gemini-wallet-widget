<#
.SYNOPSIS
  Packages a clean, portable GitHub Release ZIP containing the prebuilt Antigravity plugin,
  Windows desktop floating widget, and one-click installer scripts (zero personal paths).
#>
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$pluginManifest = Get-Content (Join-Path $projectRoot "plugin\plugin.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$version = if ($pluginManifest.version) { $pluginManifest.version } else { "1.0.0" }

$releasesDir = Join-Path $projectRoot "releases"
$pkgName = "antigravity-gemini-wallet-widget-v$version-win-x64"
$stageDir = Join-Path $releasesDir $pkgName
$zipPath = Join-Path $releasesDir "$pkgName.zip"

if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$pluginStage = Join-Path $stageDir "plugin"
New-Item -ItemType Directory -Force -Path `
    (Join-Path $pluginStage "assets"), `
    (Join-Path $pluginStage "desktop"), `
    (Join-Path $pluginStage "scripts"), `
    (Join-Path $pluginStage "server"), `
    (Join-Path $pluginStage "shared"), `
    (Join-Path $pluginStage "ui\dist") | Out-Null

# Copy clean plugin files (keep ${PLUGIN_ROOT} placeholder in hooks.json!)
Copy-Item (Join-Path $projectRoot "plugin\plugin.json") (Join-Path $pluginStage "plugin.json") -Force
Copy-Item (Join-Path $projectRoot "plugin\mcp_config.json") (Join-Path $pluginStage "mcp_config.json") -Force
Copy-Item (Join-Path $projectRoot "plugin\hooks.json") (Join-Path $pluginStage "hooks.json") -Force
Copy-Item (Join-Path $projectRoot "plugin\assets\logo.png") (Join-Path $pluginStage "assets\logo.png") -Force

Copy-Item (Join-Path $projectRoot "shared\store.mjs") (Join-Path $pluginStage "shared\store.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\hook-recorder.mjs") (Join-Path $pluginStage "scripts\hook-recorder.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\statusline-bridge.mjs") (Join-Path $pluginStage "scripts\statusline-bridge.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\sync-agy-quota.mjs") (Join-Path $pluginStage "scripts\sync-agy-quota.mjs") -Force
Copy-Item (Join-Path $projectRoot "scripts\setup-agy-statusline.ps1") (Join-Path $pluginStage "scripts\setup-agy-statusline.ps1") -Force
Copy-Item (Join-Path $projectRoot "ui\dist\app.html") (Join-Path $pluginStage "ui\dist\app.html") -Force
Copy-Item (Join-Path $projectRoot "server\mcp-server.mjs") (Join-Path $pluginStage "server\mcp-server.mjs") -Force
Copy-Item (Join-Path $projectRoot "server\package.json") (Join-Path $pluginStage "server\package.json") -Force

# Copy runtime node_modules (excluding dev-only esbuild)
$srcNm = Join-Path $projectRoot "server\node_modules"
$dstNm = Join-Path $pluginStage "server\node_modules"
New-Item -ItemType Directory -Force -Path $dstNm | Out-Null
Get-ChildItem -Path $srcNm -Force | Where-Object {
    $_.Name -notin @("esbuild", "@esbuild", ".bin")
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $dstNm $_.Name) -Recurse -Force
}

# Copy built desktop widget binaries
$desktopBuildDir = Join-Path $projectRoot "desktop\dist"
if (-not (Test-Path $desktopBuildDir)) {
    throw "Missing built desktop binaries at $desktopBuildDir. Build desktop project first."
}
Copy-Item -Path (Join-Path $desktopBuildDir "*") -Destination (Join-Path $pluginStage "desktop") -Recurse -Force

# Copy docs & license
Copy-Item (Join-Path $projectRoot "README.md") (Join-Path $stageDir "README.md") -Force
Copy-Item (Join-Path $projectRoot "LICENSE") (Join-Path $stageDir "LICENSE") -Force

# One-click installer script for end users downloading the GitHub Release
$releaseInstallerPs1 = @'
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPlugin = Join-Path $here "plugin"
$targetDir = Join-Path $env:USERPROFILE ".gemini\config\plugins\gemini-wallet-widget"

$existingExe = Join-Path $targetDir "desktop\GeminiWalletWidget.exe"
if (Test-Path $existingExe) {
    try { & $existingExe --quit | Out-Null; Start-Sleep -Milliseconds 400 } catch {}
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -Path (Join-Path $srcPlugin "*") -Destination $targetDir -Recurse -Force

# Resolve ${PLUGIN_ROOT} in hooks.json to this machine's plugin path
$hooksPath = Join-Path $targetDir "hooks.json"
$hooksRaw = Get-Content $hooksPath -Raw -Encoding UTF8
$pluginRootForward = ($targetDir -replace '\\', '/')
[System.IO.File]::WriteAllText($hooksPath, $hooksRaw.Replace('${PLUGIN_ROOT}', $pluginRootForward), [System.Text.UTF8Encoding]::new($false))

# Also configure agy statusLine if available
$setupScript = Join-Path $targetDir "scripts\setup-agy-statusline.ps1"
if (Test-Path $setupScript) {
    try { & powershell -ExecutionPolicy Bypass -File $setupScript } catch {}
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Gemini 额度猫娘挂件已成功安装到 Antigravity 插件目录！" -ForegroundColor Green
Write-Host " 路径: $targetDir"
Write-Host " 下一步: 在 Antigravity -> Plugins Manager 中启用插件并重启。"
Write-Host "============================================================" -ForegroundColor Cyan
'@
[System.IO.File]::WriteAllText((Join-Path $stageDir "一键安装到Antigravity插件.ps1"), $releaseInstallerPs1, [System.Text.UTF8Encoding]::new($false))

$releaseInstallerCmd = "@echo off`r`npowershell -ExecutionPolicy Bypass -File `"%~dp0一键安装到Antigravity插件.ps1`"`r`npause`r`n"
[System.IO.File]::WriteAllText((Join-Path $stageDir "一键安装到Antigravity插件.cmd"), $releaseInstallerCmd, [System.Text.UTF8Encoding]::new($false))

$standaloneCmd = "@echo off`r`nstart `"`" `"%~dp0plugin\desktop\GeminiWalletWidget.exe`"`r`n"
[System.IO.File]::WriteAllText((Join-Path $stageDir "直接启动桌面猫娘挂件.cmd"), $standaloneCmd, [System.Text.UTF8Encoding]::new($false))

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $stageDir -Recurse -Force

$zipInfo = Get-Item $zipPath
Write-Host ("Created GitHub Release package: {0} ({1:N2} MB)" -f $zipInfo.FullName, ($zipInfo.Length / 1MB))

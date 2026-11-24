<#
.SYNOPSIS
  Configures Antigravity CLI (`agy`) statusLine in ~/.gemini/antigravity-cli/settings.json
  so live Gemini quota JSON streams automatically into the Gemini Catgirl Wallet Widget.
#>
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$bridgePath = Join-Path $scriptDir "statusline-bridge.mjs"
if (-not (Test-Path $bridgePath)) {
    throw "Cannot find statusline-bridge.mjs at $bridgePath"
}

$agyConfigDir = Join-Path $env:USERPROFILE ".gemini\antigravity-cli"
$agySettingsPath = Join-Path $agyConfigDir "settings.json"
New-Item -ItemType Directory -Force -Path $agyConfigDir | Out-Null

$settings = @{}
if (Test-Path $agySettingsPath) {
    $raw = Get-Content -Path $agySettingsPath -Raw -Encoding UTF8
    if ($raw.Trim().Length -gt 0) {
        $parsed = $raw | ConvertFrom-Json
        foreach ($p in $parsed.PSObject.Properties) {
            $settings[$p.Name] = $p.Value
        }
    }
}

$nodeCmd = (Get-Command node -ErrorAction SilentlyContinue)
$nodeExe = if ($nodeCmd) { $nodeCmd.Source } else { "node" }
$needsNodeQuote = $nodeExe.Contains(" ")
$needsBridgeQuote = $bridgePath.Contains(" ")
if (-not $needsNodeQuote -and -not $needsBridgeQuote) {
    $commandLine = "$nodeExe $bridgePath"
} else {
    # Wrap in a tiny spaceless .cmd launcher or single cmd-safe invocation
    $shimPath = Join-Path $agyConfigDir "gemini-wallet-statusline.cmd"
    $shimContent = "@echo off`r`n`"$nodeExe`" `"$bridgePath`"`r`n"
    [System.IO.File]::WriteAllText($shimPath, $shimContent, [System.Text.UTF8Encoding]::new($false))
    $commandLine = $shimPath
}

$settings["statusLine"] = @{
    type = "command"
    command = $commandLine
}

$json = $settings | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($agySettingsPath, $json, [System.Text.UTF8Encoding]::new($false))

$agyCmd = Get-Command agy -ErrorAction SilentlyContinue
$agyDefaultBin = Join-Path $env:LOCALAPPDATA "agy\bin\agy.exe"
if ($agyCmd -or (Test-Path $agyDefaultBin)) {
    Write-Output "AGY_FOUND|已配置 agy statusLine！在终端运行 agy 后会自动同步 Gemini 实时额度。"
} else {
    Write-Output "AGY_MISSING|已写入配置，但本机未检测到 agy CLI；Antigravity 桌面端不推送额度流，请先用上方「手动校准」或安装 agy CLI 后在终端运行 agy。"
}

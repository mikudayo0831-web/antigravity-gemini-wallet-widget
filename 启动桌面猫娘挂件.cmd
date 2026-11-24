@echo off
setlocal
set "EXE=%~dp0desktop\dist\GeminiWalletWidget.exe"
if not exist "%EXE%" set "EXE=%~dp0desktop\bin\Release\GeminiWalletWidget.exe"
if not exist "%EXE%" (
  echo [ERROR] GeminiWalletWidget.exe not found at "%EXE%"
  pause
  exit /b 1
)
start "" "%EXE%"

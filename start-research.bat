@echo off
chcp 65001 >nul
title Claude Code - REA Arastirma (NVIDIA)

rem ---- Arastirma klasoru (oyun projesinin DISINDA) ----
set "RESEARCH_DIR=C:\rogueworld research"

if not defined NVIDIA_API_KEY (
  echo NVIDIA_API_KEY bulunamadi. Once setup.bat'i calistir, sonra yeni bir pencere ac.
  pause
  exit /b 1
)

rem ---- Master key'i start-claude.bat'tan oku (ayni klasorde olmali) ----
set "KEYRAW="
for /f "tokens=2 delims==" %%K in ('findstr /b /c:"set \"LITELLM_MASTER_KEY=" "%~dp0start-claude.bat"') do set "KEYRAW=%%K"
if not defined KEYRAW (
  echo start-claude.bat icinde LITELLM_MASTER_KEY bulunamadi. Bu dosya start-claude.bat ile ayni klasorde olmali.
  pause
  exit /b 1
)
set "LITELLM_MASTER_KEY=%KEYRAW:"=%"

rem ---- Proxy zaten calisiyorsa tekrar acma ----
netstat -ano | findstr ":4000 " | findstr LISTENING >nul
if errorlevel 1 (
  start "LiteLLM Proxy" cmd /k "cd /d %~dp0 && litellm --config litellm_config.yaml --port 4000"
  echo Proxy aciliyor, 15 sn bekleniyor...
  timeout /t 15 /nobreak >nul
) else (
  echo Proxy zaten calisiyor, tekrar acilmadi.
)

set "ANTHROPIC_BASE_URL=http://localhost:4000"
set "ANTHROPIC_AUTH_TOKEN=%LITELLM_MASTER_KEY%"
set "ANTHROPIC_API_KEY="
set "ANTHROPIC_MODEL=nvidia-main"
set "ANTHROPIC_DEFAULT_OPUS_MODEL=nvidia-main"
set "ANTHROPIC_DEFAULT_SONNET_MODEL=nvidia-main"
set "ANTHROPIC_DEFAULT_HAIKU_MODEL=nvidia-fast"
set "ANTHROPIC_SMALL_FAST_MODEL=nvidia-fast"
set "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1"

if not exist "%RESEARCH_DIR%" mkdir "%RESEARCH_DIR%"
cd /d "%RESEARCH_DIR%"
claude

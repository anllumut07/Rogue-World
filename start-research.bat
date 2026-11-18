@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
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

rem ---- Python/LiteLLM UTF-8 zorlamasi ----
rem chcp 65001 YALNIZCA konsola yazarken gecerli. Proxy cikisini bir dosyaya
rem YONLENDIRDIIGIMIZ icin Python stdout kodlamasini locale'den aliyor (cp1252)
rem ve banner'daki █ ╗ ╝ karakterleri -> UnicodeEncodeError -> proxy ayaga kalkamiyor.
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"

rem ---- Proxy zaten calisiyorsa tekrar acma ----
netstat -ano | findstr ":4000 " | findstr LISTENING >nul
if errorlevel 1 (
  start "LiteLLM Proxy" cmd /k "cd /d %~dp0 && litellm --config litellm_config.yaml --port 4000"
  echo Proxy aciliyor, hazir olana kadar bekleniyor...
  rem 'timeout' yonlendirilmis stdin'de aninda basarisiz olur; ping ile bekle.
  set "READY=0"
  for /l %%i in (1,1,40) do (
    if "!READY!"=="0" (
      curl.exe -s -f -o nul http://127.0.0.1:4000/health/liveliness
      if not errorlevel 1 (
        set "READY=1"
      ) else (
        %SystemRoot%\System32\ping.exe -n 3 127.0.0.1 >nul
      )
    )
  )
  if "!READY!"=="0" (
    echo HATA: Proxy 4000 portunda cevap vermiyor.
    pause
    exit /b 1
  )
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

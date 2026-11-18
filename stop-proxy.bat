@echo off
chcp 65001 >nul
echo LiteLLM surecleri kapatiliyor...
taskkill /F /IM litellm.exe >nul 2>nul
for /f "tokens=5" %%P in ('netstat -ano ^| findstr ":4000 " ^| findstr LISTENING') do taskkill /F /PID %%P >nul 2>nul
echo Tamam. Port 4000 durumu:
netstat -ano | findstr ":4000 " | findstr LISTENING || echo   4000 portu bos.
pause

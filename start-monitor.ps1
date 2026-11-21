<#
.SYNOPSIS
    Ruv-Swarm Monitor Auto-Start Script
.DESCRIPTION
    Bu script Claude Code oturumu başladığında ruv-swarm monitor daemon'ını
    arka planda başlatır ve terminal tabanlı UI'yi gösterir.
#>

param(
    [int]$Port = 8765,
    [float]$Interval = 2.0,
    [switch]$ShowUI = $true,
    [switch]$DaemonOnly = $false
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$MonitorScript = Join-Path $ScriptDir "ruv-swarm-monitor-daemon.py"
$UI_Script = Join-Path $ScriptDir "ruv-swarm-monitor.py"

# Python yolunu bul
$PythonPath = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $PythonPath) {
    $PythonPath = (Get-Command python3 -ErrorAction SilentlyContinue).Source
}
if (-not $PythonPath) {
    Write-Error "Python bulunamadı! Lütfen Python yükleyin."
    exit 1
}

Write-Host "🔮 Ruv-Swarm Monitor başlatılıyor..." -ForegroundColor Cyan
Write-Host "Python: $PythonPath" -ForegroundColor Gray

# Daemon'ı arka planda başlat
$DaemonArgs = @(
    $MonitorScript,
    "--port", $Port,
    "--interval", $Interval
)

Write-Host "Daemon başlatılıyor (port $Port)..." -ForegroundColor Yellow

$DaemonProcess = Start-Process -FilePath $PythonPath -ArgumentList $DaemonArgs `
    -WindowStyle Hidden -PassThru

if (-not $DaemonProcess) {
    Write-Error "Daemon başlatılamadı!"
    exit 1
}

Write-Host "✅ Daemon başlatıldı (PID: $($DaemonProcess.Id))" -ForegroundColor Green
Write-Host "✅ Dashboard: http://localhost:$Port" -ForegroundColor Green
Write-Host "✅ API: http://localhost:$Port/api/all" -ForegroundColor Green

# Daemon'ın hazır olması için bekle
Start-Sleep -Seconds 2

if ($DaemonOnly) {
    Write-Host "Daemon arka planda çalışıyor. Kapatmak için: Stop-Process -Id $($DaemonProcess.Id)" -ForegroundColor Yellow
    exit 0
}

# Terminal UI'yi başlat (eğer isteniyorsa)
if ($ShowUI) {
    Write-Host "`n🖥️ Terminal UI başlatılıyor... (Çıkmak için Ctrl+C)" -ForegroundColor Cyan
    Start-Sleep -Seconds 1

    $UIArgs = @(
        $UI_Script,
        "--interval", $Interval
    )

    try {
        # UI'yi aynı pencerede çalıştır
        & $PythonPath $UIArgs
    }
    catch {
        Write-Error "UI hatası: $_"
    }
    finally {
        # UI kapanınca daemon'ı da kapat
        Write-Host "`n🛑 UI kapatıldı, daemon durduruluyor..." -ForegroundColor Yellow
        Stop-Process -Id $DaemonProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

# Cleanup function
function Cleanup {
    if ($DaemonProcess -and $DaemonProcess.HasExited -eq $false) {
        Stop-Process -Id $DaemonProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

# Register cleanup on exit
$global:DaemonPID = $DaemonProcess.Id
Register-EngineEvent -SourceIdentifier "PowerShell.Exiting" -Action { Cleanup } | Out-Null
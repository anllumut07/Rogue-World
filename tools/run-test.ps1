# Rogue World — headless test koşucu
# Kullanim: powershell -File tools\run-test.ps1 --disaster-test [--scenario=X] [--country=X] [--speed=X]
# Godot konsol exe sabit yolda calistirilir, cikis test-logs/<bayrak>.log / .err ye yazilir.

param(
    [string]$Scenario,
    [string]$Country,
    [string]$Speed
)

$TestFlag = if ($args.Count -gt 0) { $args[0] } else {
    Write-Error "Test bayrağı zorunlu: --disaster-test, --save-load-test, --intervention-test vb."
    exit 2
}

$GodotExe = "C:\Users\rogue\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
$RepoRoot = (Get-Item .).FullName
$LogDir   = Join-Path $RepoRoot "test-logs"

if (-not (Test-Path $GodotExe)) {
    Write-Error "Godot konsol exe bulunamadi: $GodotExe"
    exit 2
}

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir | Out-Null
}

$SafeFlag = $TestFlag.TrimStart("-").Replace("--", "").Replace("-", "_")
$LogPath  = Join-Path $LogDir "$SafeFlag.log"
$ErrPath  = Join-Path $LogDir "$SafeFlag.err"

$Args = @("--headless", "--path", ".", "--", $TestFlag)
if ($PSBoundParameters.ContainsKey("Scenario")) { $Args += "--scenario=$Scenario" }
if ($PSBoundParameters.ContainsKey("Country"))   { $Args += "--country=$Country" }
if ($PSBoundParameters.ContainsKey("Speed"))     { $Args += "--speed=$Speed" }

Write-Host "=== TEST BASLATILIYOR: $TestFlag ==="
Write-Host "Godot   : $GodotExe"
Write-Host "Args    : $Args"
Write-Host "Log     : $LogPath"
Write-Host "Error   : $ErrPath"

$Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$Process = Start-Process -FilePath $GodotExe -ArgumentList $Args -WorkingDirectory $RepoRoot `
    -RedirectStandardOutput $LogPath -RedirectStandardError $ErrPath -PassThru

$Waited = $Process.WaitForExit(120000) # 120 sn
$Stopwatch.Stop()

if (-not $Waited) {
    Write-Warning "Godot 120 sn'de bitmedi, zorla sonlandiriliyor."
    Stop-Process -Id $Process.Id -Force
    $ExitCode = -1
} else {
    $ExitCode = $Process.ExitCode
}

# Godot surecini kesinlikle sonlandir (tek Godot suresi kurali)
Get-Process | Where-Object { $_.ProcessName -like "Godot*" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== TEST SONU: $TestFlag ==="
Write-Host "Suresi  : $($Stopwatch.Elapsed.TotalSeconds) sn"
Write-Host "Cikis kodu: $ExitCode"

# Log'daki SONUÇ satirini ekrana yaz
if (Test-Path $LogPath) {
    $ResultLine = Select-String -Path $LogPath -Pattern "SONUÇ:" -Encoding utf8 | Select-Object -First 1
    if ($ResultLine) { Write-Host "Sonuc   : $($ResultLine.Line)" }
    else { Write-Host "Sonuc   : (LOGDA SONUÇ satiri bulunamadi, log dosyasina bakin: $LogPath)" }
} else {
    Write-Host "Sonuc   : (LOG DOSYASI YOK)"
}

if (Test-Path $ErrPath) {
    $ErrTail = Get-Content $ErrPath -Tail 5 -ErrorAction SilentlyContinue
    if ($ErrTail) { Write-Host "STDERR  : $ErrTail" }
}

if ($ResultLine -and $ResultLine.Line -like "*PASS*") { $ExitCode = 0 }
if ($ExitCode -eq 0) { Write-Host "DURUM: PASS" } else { Write-Host "DURUM: FAIL (exit $ExitCode)" }
exit $ExitCode


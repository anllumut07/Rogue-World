# tools/stop-guard.ps1 — Stop hook emniyeti (Otonom mod)
# Claue Code Stop hook'undan stdin girdisini okur (tüketilir, karar üzerinde etki yok).
# Aşağıdaki üç koşuldan biri doğruysa duruma izin verilir (sessiz exit 0):
#   1) Proje kökünde STOP.txt varsa
#   2) Zaman 2026-10-20 10:00'ye ulaştıysa/geçtiyse
#   3) test-logs/stop-guard-count.txt sayacı 400'u geçtiyse (sonsuz döngü emniyeti)
# Aksi halde engelleme JSON'u basılır ve durdurma engellenir.

$input | Out-Null

$RepoRoot = (Get-Item .).FullName
$LogDir   = Join-Path $RepoRoot "test-logs"
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir | Out-Null
}
$CountFile = Join-Path $LogDir "stop-guard-count.txt"

$count = 0
if (Test-Path $CountFile) {
    $count = [int](Get-Content $CountFile -ErrorAction SilentlyContinue)
}
$count++
$count | Out-File -FilePath $CountFile -Encoding utf8

$StopFile  = Join-Path $RepoRoot "STOP.txt"
$EndTime   = [datetime]'2026-10-20 10:00'

if (Test-Path $StopFile) { exit 0 }
if ((Get-Date) -ge $EndTime) { exit 0 }
if ($count -gt 400) { exit 0 }

$block = '{"decision":"block","reason":"Bitiş zamanı gelmedi ve STOP.txt yok. Durma, onay isteme. TODO.md''deki sıradaki bitmemiş alt göreve hemen geç ve bitiş zamanına kadar çalış."}'
$block
exit 0

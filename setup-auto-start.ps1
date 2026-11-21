<#
.SYNOPSIS
    Ruv-Swarm Monitor - Otomatik Başlatma Kurulumu
.DESCRIPTION
    Bu script:
    1. PowerShell profiline auto-start ekler
    2. Bash/Git Bash profiline auto-start ekler
    3. Claude Code hooks'e session-start hook'u ekler
    4. Kısayol komutları oluşturur
#>

param(
    [switch]$Force = $false
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$StartScript = Join-Path $ScriptDir "start-monitor.ps1"

Write-Host "🔧 Ruv-Swarm Monitor - Otomatik Başlatma Kurulumu" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Gray

# 1. PowerShell Profile
$PSProfile = $PROFILE.CurrentUserAllHosts
if (-not (Test-Path $PSProfile)) {
    New-Item -Path $PSProfile -ItemType File -Force | Out-Null
}

$PSProfileContent = @"
# >>> Ruv-Swarm Monitor Auto-Start >>>
if (Test-Path "$StartScript") {
    # Sadece interaktif shell'de ve Claude Code içinde çalıştır
    if (\$Host.Name -eq 'ConsoleHost' -or \$env:CLAUDE_CODE_SESSION) {
        # Daemon'ı arka planda başlat (UI gösterme)
        Start-Process powershell -ArgumentList "-NoProfile -Command `"& '$StartScript' -DaemonOnly`"" -WindowStyle Hidden
        Write-Host "🔮 Ruv-Swarm Monitor daemon arka planda başlatıldı (http://localhost:8765)" -ForegroundColor Cyan
    }
}
# <<< Ruv-Swarm Monitor Auto-Start <<<
"@

if ($Force -or (Get-Content $PSProfile -Raw) -notmatch "Ruv-Swarm Monitor Auto-Start") {
    Add-Content -Path $PSProfile -Value "`n$PSProfileContent"
    Write-Host "✅ PowerShell profiline eklendi: $PSProfile" -ForegroundColor Green
} else {
    Write-Host "ℹ️ PowerShell profiline zaten eklenmiş" -ForegroundColor Yellow
}

# 2. Bash Profile (Git Bash / WSL)
$BashProfile = "$env:USERPROFILE\.bash_profile"
if (-not (Test-Path $BashProfile)) {
    $BashProfile = "$env:USERPROFILE\.bashrc"
}
if (-not (Test-Path $BashProfile)) {
    New-Item -Path $BashProfile -ItemType File -Force | Out-Null
}

$BashProfileContent = @"
# >>> Ruv-Swarm Monitor Auto-Start >>>
if [[ -f "$ScriptDir/start-monitor.ps1" ]]; then
    # Sadece interaktif shell'de başlat
    if [[ \$- == *i* ]]; then
        # Daemon'ı arka planda başlat
        nohup powershell.exe -NoProfile -Command "& '$StartScript' -DaemonOnly" > /dev/null 2>&1 &
        echo -e "\033[36m🔮 Ruv-Swarm Monitor daemon arka planda başlatıldı (http://localhost:8765)\033[0m"
    fi
fi
# <<< Ruv-Swarm Monitor Auto-Start <<<
"@

if ($Force -or (Get-Content $BashProfile -Raw) -notmatch "Ruv-Swarm Monitor Auto-Start") {
    Add-Content -Path $BashProfile -Value "`n$BashProfileContent"
    Write-Host "✅ Bash profiline eklendi: $BashProfile" -ForegroundColor Green
} else {
    Write-Host "ℹ️ Bash profiline zaten eklenmiş" -ForegroundColor Yellow
}

# 3. Claude Code Hooks - Session Start
$ClaudeSettingsDir = "$env:USERPROFILE\.claude"
$SettingsFile = Join-Path $ClaudeSettingsDir "settings.json"
$HooksDir = Join-Path $ClaudeSettingsDir "hooks"

if (-not (Test-Path $HooksDir)) {
    New-Item -Path $HooksDir -ItemType Directory -Force | Out-Null
}

# Hook script
$HookScript = Join-Path $HooksDir "session-start-ruv-monitor.ps1"
$HookContent = @"
param(
    [string]\$SessionId
)

# Ruv-Swarm Monitor daemon başlat
\$StartScript = "$StartScript"
if (Test-Path \$StartScript) {
    Start-Process powershell -ArgumentList "-NoProfile -Command `"`& '\$StartScript' -DaemonOnly`"" -WindowStyle Hidden
    Write-Host \"🔮 Ruv-Swarm Monitor started for session \$SessionId\" -ForegroundColor Cyan
}
"@

Set-Content -Path $HookScript -Value $HookContent -Encoding UTF8
Write-Host "✅ Hook script oluşturuldu: $HookScript" -ForegroundColor Green

# Settings.json güncelle
if (Test-Path $SettingsFile) {
    $Settings = Get-Content $SettingsFile -Raw | ConvertFrom-Json
} else {
    $Settings = @{} | ConvertTo-Json -Depth 10 | ConvertFrom-Json
}

if (-not $Settings.hooks) { $Settings.hooks = @{} }
if (-not $Settings.hooks.session_start) { $Settings.hooks.session_start = @() }

$HookEntry = @{
    "matcher" = ".*"
    "hooks" = @(
        @{
            "type" = "command"
            "command" = "powershell.exe -NoProfile -File `"$HookScript`" --session-id \${SESSION_ID}"
        }
    )
}

# Check if already exists
$Exists = $false
foreach ($h in $Settings.hooks.session_start) {
    if ($h.hooks[0].command -like "*session-start-ruv-monitor*") {
        $Exists = $true
        break
    }
}

if (-not $Exists -or $Force) {
    $Settings.hooks.session_start += $HookEntry
    $Settings | ConvertTo-Json -Depth 10 | Set-Content $SettingsFile -Encoding UTF8
    Write-Host "✅ Claude Code settings.json güncellendi" -ForegroundColor Green
} else {
    Write-Host "ℹ️ Hook zaten mevcut" -ForegroundColor Yellow
}

# 4. Kısayol komutları (alias)
Write-Host "`n📋 Kullanılabilir Komutlar:" -ForegroundColor Cyan
Write-Host "  ruv-monitor       - Terminal UI başlat" -ForegroundColor Gray
Write-Host "  ruv-monitor-daemon - Sadece daemon başlat" -ForegroundColor Gray
Write-Host "  ruv-dashboard     - Web dashboard aç" -ForegroundColor Gray
Write-Host "  ruv-status        - Hızlı durum kontrolü" -ForegroundColor Gray

# PowerShell alias'ları (geçici - kalıcı için profile eklenebilir)
Set-Alias -Name ruv-monitor -Value "$StartScript -ShowUI" -Force -Option AllScope -ErrorAction SilentlyContinue
Set-Alias -Name ruv-monitor-daemon -Value "$StartScript -DaemonOnly" -Force -Option AllScope -ErrorAction SilentlyContinue
Set-Alias -Name ruv-dashboard -Value "start http://localhost:8765" -Force -Option AllScope -ErrorAction SilentlyContinue
Set-Alias -Name ruv-status -Value "powershell -NoProfile -Command `& '$ScriptDir\ruv-swarm-monitor-daemon.py' --once`" -Force -Option AllScope -ErrorAction SilentlyContinue

Write-Host "`n✅ Kurulum tamamlandı!" -ForegroundColor Green
Write-Host "`n📝 Notlar:" -ForegroundColor Yellow
Write-Host "  - Yeni terminal/oturum açtığınızda daemon otomatik başlar" -ForegroundColor Gray
Write-Host "  - Terminal UI için: ruv-monitor yazın" -ForegroundColor Gray
Write-Host "  - Web dashboard: http://localhost:8765" -ForegroundColor Gray
Write-Host "  - Değişikliklerin etkili olması için terminali yeniden başlatın" -ForegroundColor Gray
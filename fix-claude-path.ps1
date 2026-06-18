# ============================================================================
# fix-claude-path.ps1
# Locate Claude Code (claude.exe or claude.cmd) and add it to the USER PATH
# so 'claude' works in every new PowerShell window.
# ----------------------------------------------------------------------------
# HOW TO RUN (on the laptop with the broken PATH):
#   1. Plug this USB in.
#   2. Open Windows PowerShell  (does NOT need to be Admin - user PATH only).
#   3. cd F:\          (or whatever letter the USB shows up as)
#   4. Set-ExecutionPolicy -Scope Process Bypass -Force
#   5. .\fix-claude-path.ps1
#   6. CLOSE that PowerShell window and open a NEW one, then:
#        claude --version
# ============================================================================

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Claude Code PATH repair" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# All known places the Claude installer / npm might have dropped the binary.
$candidates = @(
    "$env:USERPROFILE\.local\bin\claude.exe",
    "$env:USERPROFILE\.local\bin\claude.cmd",
    "$env:LOCALAPPDATA\Programs\claude\claude.exe",
    "$env:LOCALAPPDATA\Anthropic\Claude\claude.exe",
    "$env:LOCALAPPDATA\AnthropicClaude\claude.exe",
    "$env:APPDATA\npm\claude.cmd",
    "$env:APPDATA\npm\claude.ps1",
    "$env:ProgramFiles\Claude\claude.exe",
    "${env:ProgramFiles(x86)}\Claude\claude.exe"
)

$found = $null
Write-Host "Searching known install locations..." -ForegroundColor Yellow
foreach ($p in $candidates) {
    if (Test-Path $p) { Write-Host "  FOUND: $p" -ForegroundColor Green; $found = $p; break }
    else              { Write-Host "  no:    $p" -ForegroundColor DarkGray }
}

# Fallback - recursive search under USERPROFILE if nothing matched.
if (-not $found) {
    Write-Host ""
    Write-Host "Not in standard spots - searching under $env:USERPROFILE ..." -ForegroundColor Yellow
    $hit = Get-ChildItem -Path $env:USERPROFILE `
        -Include "claude.exe","claude.cmd" -Recurse -ErrorAction SilentlyContinue -Force `
        | Select-Object -First 1
    if ($hit) { $found = $hit.FullName; Write-Host "  FOUND: $found" -ForegroundColor Green }
}

if (-not $found) {
    Write-Host ""
    Write-Host "ERROR: Could not find claude.exe / claude.cmd anywhere." -ForegroundColor Red
    Write-Host "Re-run the Claude installer first:" -ForegroundColor Red
    Write-Host "  irm https://claude.ai/install.ps1 | iex" -ForegroundColor Red
    exit 1
}

$claudeDir = Split-Path $found -Parent
Write-Host ""
Write-Host "Claude lives in:  $claudeDir" -ForegroundColor Cyan

# Read current USER PATH (NOT machine - we don't need admin)
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if (-not $userPath) { $userPath = "" }

$parts = $userPath.Split(';') | Where-Object { $_ -ne "" }
if ($parts -contains $claudeDir) {
    Write-Host "Already on User PATH - nothing to add." -ForegroundColor Green
} else {
    $newPath = if ($userPath) { "$userPath;$claudeDir" } else { $claudeDir }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Added to User PATH (persists across reboots)." -ForegroundColor Green
}

# Update THIS session too, so a quick test works without reopening.
if (-not (($env:Path.Split(';')) -contains $claudeDir)) {
    $env:Path = "$env:Path;$claudeDir"
}

Write-Host ""
Write-Host "Testing claude in this session..." -ForegroundColor Yellow
try {
    & $found --version
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " SUCCESS.  Close this PowerShell window and open a NEW one." -ForegroundColor Green
    Write-Host " Then just type:   claude" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
} catch {
    Write-Host "claude found at $found but won't run: $($_.Exception.Message)" -ForegroundColor Red
}

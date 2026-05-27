# ============================================================================
# Setup script for a fresh Windows laptop
# ----------------------------------------------------------------------------
# Copy this file to a USB stick. On the new laptop:
#   1. Plug the USB in
#   2. Open Windows PowerShell as Administrator  (right-click -> Run as admin)
#   3. cd E:\           (or whatever drive letter the USB shows up as)
#   4. Set-ExecutionPolicy -Scope Process Bypass -Force
#   5. .\setup-new-laptop.ps1
#
# Then close PowerShell and reopen it so PATH changes take effect.
# ============================================================================

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " Newton4thGui / Claude Code laptop setup" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Section 1 - Prerequisites required by Claude Code
# ---------------------------------------------------------------------------
# The Claude Code installer fails on a bare Windows install because it needs
# either Git's bash.exe or PowerShell 7 to run its hooks. We install both so
# we never get bitten by it again.

Write-Host "[1/4] Installing Git for Windows ..." -ForegroundColor Yellow
winget install --id Git.Git -e `
    --silent --accept-source-agreements --accept-package-agreements

Write-Host "[2/4] Installing PowerShell 7 ..." -ForegroundColor Yellow
winget install --id Microsoft.PowerShell -e `
    --silent --accept-source-agreements --accept-package-agreements

Write-Host "[3/4] Installing Node.js LTS  (Claude Code runtime) ..." -ForegroundColor Yellow
winget install --id OpenJS.NodeJS.LTS -e `
    --silent --accept-source-agreements --accept-package-agreements

# Refresh PATH for this session so the next step can see the new tools.
$env:Path = `
    [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + `
    [Environment]::GetEnvironmentVariable("Path","User")

# ---------------------------------------------------------------------------
# Section 2 - Claude Code itself
# ---------------------------------------------------------------------------
Write-Host "[4/4] Installing Claude Code ..." -ForegroundColor Yellow
Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression

# ---------------------------------------------------------------------------
# OPTIONAL: only uncomment the block below if you also want to BUILD the
# Newton4thGui app from source on this laptop (i.e. develop, not just run it).
# If you're only going to install the packaged Newton4thGui-Setup.exe, you
# do NOT need the .NET SDK.
# ---------------------------------------------------------------------------
# Write-Host "Installing .NET 8 SDK (dev only) ..." -ForegroundColor Yellow
# winget install --id Microsoft.DotNet.SDK.8 -e `
#     --silent --accept-source-agreements --accept-package-agreements

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " DONE.  Close this PowerShell window and open a NEW one,"   -ForegroundColor Green
Write-Host " then run:  claude --version"                                -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""

==============================================================
  NEW LAPTOP SETUP - QUICK REFERENCE
==============================================================

WHAT THE FILES ARE
------------------
  setup-new-laptop.ps1            PowerShell script - installs Git,
                                  PowerShell 7, Node.js, Claude Code.
  README-new-laptop.txt           This file.
  Newton4thGui-Setup-1.0.0.exe    The actual app installer (once
                                  it is built - bundles .NET 8 +
                                  FTDI driver, no prereqs needed).


WHAT YOU NEED ON THE OTHER LAPTOP FOR EACH SCENARIO
---------------------------------------------------

A) "I just want to run the app (PPA5500 + Rotronic logger)"
   --> Copy Newton4thGui-Setup-1.0.0.exe to USB, double-click,
       click Next/Install. That's it. No PowerShell, no scripts,
       no extra downloads.

B) "I also want Claude Code on the other laptop"
   --> Run setup-new-laptop.ps1 as Administrator (see steps below).
       This installs:
         * Git for Windows         (needed by Claude Code)
         * PowerShell 7            (better shell)
         * Node.js LTS             (Claude Code runtime)
         * Claude Code             (the CLI you used here)

C) "I want to build/develop the app from source on the other laptop"
   --> Run setup-new-laptop.ps1, then uncomment the .NET 8 SDK
       block at the bottom of the script and run it again.


HOW TO RUN setup-new-laptop.ps1
-------------------------------
  1. Plug the USB stick in.
  2. Open Windows PowerShell as Administrator
     (Start menu -> type "powershell" -> right-click -> Run as admin).
  3. Change to the USB drive, e.g.
        cd E:\
     (use whatever letter the USB shows up as in File Explorer).
  4. Allow the script to run for this session only:
        Set-ExecutionPolicy -Scope Process Bypass -Force
  5. Run it:
        .\setup-new-laptop.ps1
  6. When it finishes, close PowerShell and open a NEW window
     (so the new PATH is picked up), then test:
        claude --version


WHY YOUR EARLIER ATTEMPT FAILED
-------------------------------
The screenshot showed the Claude Code installer saying it needed
"Git for Windows (for bash) or PowerShell 7". You ran 'claude'
straight after, but two things were wrong:
  (1) Neither prereq was installed, so the install was incomplete.
  (2) Even when the install completes, you must close and reopen
      PowerShell - the running shell does not see PATH changes
      that happen inside it.
This script fixes both: it installs the prereqs FIRST, then runs
the Claude Code installer, and reminds you to reopen the shell.

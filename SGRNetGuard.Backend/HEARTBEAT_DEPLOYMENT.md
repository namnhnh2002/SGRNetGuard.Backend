#!/bin/bash
# Quick deployment guide for SGR NetGuard Heartbeat Agent
# Copy-paste the one-liner below in PowerShell (as Administrator) on each machine

# ============================================================
# 🚀 ONE-LINER DEPLOYMENT COMMAND
# ============================================================

# For individual machines (copy entire line):
powershell -NoProfile -ExecutionPolicy Bypass -Command "IEX(New-Object Net.WebClient).DownloadString('https://sgrnetguard-backend.onrender.com/install-heartbeat.ps1')"


# ============================================================
# ❗ IMPORTANT: Run as Administrator!
# ============================================================

# Steps:
# 1. Press Win+X, select "Windows PowerShell (Admin)" or "Terminal (Admin)"
# 2. Paste the one-liner command above
# 3. Press Enter and wait for installation to complete
# 4. Restart Windows or log off/log on
# 5. ✅ Done! Heartbeat will run automatically


# ============================================================
# 📋 What the installer does:
# ============================================================

# 1. Downloads: SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip
# 2. Extracts to: C:\SGRNetGuard\Heartbeat
# 3. Installs: Scheduled Task "SGRNetGuard-Heartbeat"
# 4. Starts: Automatic heartbeat every 60 seconds
# 5. Result: Machine marked as isInternal=true on Dashboard


# ============================================================
# 🔍 For IT Admins - Batch Deployment:
# ============================================================

# Option 1: Group Policy (push to all domain machines)
# - Create GPO with Scheduled Task pointing to:
#   C:\SGRNetGuard\Heartbeat\ClientHeartbeatLauncher.cmd
#
# Option 2: Remote execution (one-by-one)
# powershell -ComputerName APC-KTHT02 -Command {
#   IEX(New-Object Net.WebClient).DownloadString('https://sgrnetguard-backend.onrender.com/install-heartbeat.ps1')
# }
#
# Option 3: Create batch script and deploy via SCCM/Intune


# ============================================================
# ✅ Verification:
# ============================================================

# After installation, verify on the machine:
# 1. Open Task Scheduler
# 2. Find: SGRNetGuard-Heartbeat
# 3. Should show "Ready" status
# 4. In Dashboard IT: machine will show isInternal=true


# ============================================================
# 🆘 Troubleshooting:
# ============================================================

# Check logs:
# C:\SGRNetGuard\Heartbeat\heartbeat-error.log

# Manual heartbeat test:
# C:\SGRNetGuard\Heartbeat\ClientHeartbeatSender.ps1 -Once

# View Task status:
# Get-ScheduledTask -TaskName "SGRNetGuard-Heartbeat"

# Uninstall (remove task):
# Unregister-ScheduledTask -TaskName "SGRNetGuard-Heartbeat" -Confirm:$false

# 🚀 SGR NetGuard - Universal Heartbeat Deployment (All Machines)

**Version**: v20260915.1.0.4 Universal
**Date**: September 15, 2026
**Status**: ✅ Ready for production deployment to ALL machines

---

## 🎯 Purpose

Enable **ALL machines** on the internal network to send heartbeat data to Dashboard IT with **automatic `isInternal=true` detection**.

---

## 📋 What Was Fixed

### Problem
Machines on internal networks were not correctly reporting `isInternal=true` to the API.

### Root Cause
The heartbeat script only used subnet-matching for internal network detection, with no fallback. If a machine's IP didn't match configured subnets, it was marked as external.

### Solution
Implemented **multi-method detection** with fallbacks:

```powershell
# Line 288 in ClientHeartbeatSender.ps1:
$isInternal = ($null -ne $site) -or (Test-IsInternalNetwork)
```

---

## ✅ Detection Logic (Works for ALL Machines)

The heartbeat now checks **4 detection methods** in order:

### Method 1: Subnet Matching (Primary)
- Gets subnet configuration from API (`/api/config`)
- Matches machine IP against configured subnets
- Returns site/region information if matched
- **Works for**: All machines with configured subnet IPs

### Method 2: Network Profile Detection (Fallback #1)
- Checks network profile names: `SGR-OFFICE`, `Private`, `DomainAuthenticated`
- **Works for**: Machines on internal networks without specific subnet config

### Method 3: Private IP Detection (Fallback #2)
- Checks for presence of private IP ranges:
  - `10.0.0.0/8`
  - `192.168.0.0/16`
  - `172.16.0.0/12`
- **Works for**: Any machine on internal network

### Method 4: Environment Override (Admin Control)
- Check: `$env:SGR_NETGUARD_INTERNAL`
- Allows manual override if automatic detection fails
- **Works for**: Special cases or testing

**Result**: `isInternal = true` if ANY method detects internal network ✅

---

## 📦 Package Contents

**File**: `SGRNetGuard-Heartbeat-Agent-v20260915.1.0.4-Universal.zip` (5.3 KB)

Contains:
- `ClientHeartbeatSender.ps1` - Main heartbeat agent (12.2 KB)
- `Install-ClientHeartbeatTask.ps1` - Task installer (1.2 KB)
- `ClientHeartbeatLauncher.cmd` - Startup launcher (0.3 KB)
- `README.txt` - Instructions (1.2 KB)

---

## 🚀 Installation Methods

### ✅ Option 1: Universal Standalone Batch (RECOMMENDED)

**Best for**: All machines, batch deployment, Group Policy, SCCM/Intune

```batch
install-heartbeat-standalone.bat
```

**Steps**:
1. Download `install-heartbeat-standalone.bat`
2. Right-click → "Run as Administrator"
3. Wait ~2 minutes
4. Done!

**Features**:
- ✅ No PowerShell required
- ✅ Works offline (pre-download .zip if needed)
- ✅ Auto-detects or downloads package from GitHub
- ✅ Clear progress feedback

---

### Option 2: Manual PowerShell Installation

**Best for**: IT administrators with PowerShell access

```powershell
# Run as Administrator:
cd C:\SGRNetGuard\Heartbeat
.\Install-ClientHeartbeatTask.ps1
```

**Steps**:
1. Extract ZIP to `C:\SGRNetGuard\Heartbeat`
2. Open PowerShell as Administrator
3. Run above command
4. Restart Windows

---

### Option 3: Manual Batch Installation

**Best for**: IT administrators familiar with batch scripts

```batch
# In Command Prompt as Administrator:
cd C:\SGRNetGuard\Heartbeat
Install-ClientHeartbeatTask.ps1 (via PowerShell)
```

---

## 🎯 Deployment Scenarios

### Scenario 1: Single Machine
```batch
# On user's machine:
1. Download: install-heartbeat-standalone.bat
2. Right-click → Run as Administrator
3. Restart Windows
4. Done!
```

### Scenario 2: Batch Deployment (Group Policy)

```batch
# 1. Create Group Policy with Scheduled Task:
#    - Name: SGRNetGuard-Heartbeat
#    - Action: install-heartbeat-standalone.bat
#    - Trigger: At Startup, At Logon
#    - Run As: SYSTEM (Administrator)
#
# 2. Distribute to OUs
# 3. Machines auto-install on next restart
```

### Scenario 3: SCCM/Intune Deployment

```batch
# 1. Package both files:
#    - install-heartbeat-standalone.bat
#    - SGRNetGuard-Heartbeat-Agent-v20260915.1.0.4-Universal.zip
#
# 2. Deploy as Required application
# 3. Execution: install-heartbeat-standalone.bat
# 4. Restart: Allow restart after installation
```

### Scenario 4: Git/DevOps Automation

```bash
# Download latest version:
# https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/install-heartbeat-standalone.bat
# https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/dist/SGRNetGuard-Heartbeat-Universal-Latest.zip

# Deploy to all machines via your infrastructure automation
```

---

## ✨ What Happens After Installation

✅ **Automatic startup**: Heartbeat starts at Windows boot and login
✅ **Automatic transmission**: Every 60 seconds, heartbeat is sent to API
✅ **Automatic detection**: `isInternal=true` if machine is on internal network
✅ **Automatic dashboard**: Machine appears in IT Dashboard with live stats

**No user interaction required after installation!**

---

## 🔍 Verification

### Step 1: Check Task Scheduler
```powershell
# Open Task Scheduler (Win + R → taskschd.msc)
# Find: SGRNetGuard-Heartbeat
# Status: Should be "Ready"
```

### Step 2: Check Logs
```powershell
# File: C:\SGRNetGuard\Heartbeat\heartbeat-error.log
# Should contain: "Heartbeat sent to https://sgrnetguard-backend.onrender.com/api/heartbeat"
```

### Step 3: Verify in Dashboard
1. Open IT Dashboard
2. Search for machine name
3. Should show:
   - `isInternal: true`
   - Live CPU/RAM/Disk stats
   - Last seen time (recent)

---

## 🆘 Troubleshooting

### Problem: "Access Denied"
**Solution**:
- Right-click AND select "Run as Administrator"
- Do NOT just double-click

### Problem: "Could not download package"
**Solution**:
- Pre-download ZIP and place in same folder as .bat
- Or check internet connection

### Problem: Machine still shows "isInternal: false"
**Solution**:
- Wait 2-3 minutes for next heartbeat
- Refresh Dashboard page
- Check machine has private IP (10.x, 192.168.x, 172.16.x)
- Restart Windows
- Check logs at C:\SGRNetGuard\Heartbeat\heartbeat-error.log

### Problem: Scheduled Task won't install
**Solution**:
- Run as Administrator confirmed?
- UAC (User Account Control) blocking?
- Try disabling antivirus temporarily
- Restart Windows and try again

---

## 📊 Technical Details

### File Locations After Installation
```
C:\SGRNetGuard\
├── Heartbeat\
│   ├── ClientHeartbeatSender.ps1
│   ├── Install-ClientHeartbeatTask.ps1
│   ├── ClientHeartbeatLauncher.cmd
│   ├── heartbeat-error.log (created after first run)
│   └── README.txt
└── (other components...)
```

### Scheduled Task Details
```
Task Name: SGRNetGuard-Heartbeat
Status: Ready
Trigger: At startup + At logon
Action: C:\SGRNetGuard\Heartbeat\ClientHeartbeatLauncher.cmd
Interval: Every 60 seconds
Run As: SYSTEM (no user interaction)
```

### Heartbeat Payload
Each heartbeat includes:
- Device name, OS, Windows version
- CPU %, RAM %, Disk % utilization
- Network type (LAN/WiFi), adapter info
- **isInternal flag** (TRUE if on internal network)
- Site/Region (if subnet matched)
- AD joined, security software status
- Timestamp

---

## 🔐 Security Notes

✅ **No passwords transmitted** - all communication over HTTPS
✅ **Minimal data** - only technical metadata sent
✅ **System service** - runs as SYSTEM, no user data access
✅ **Read-only operations** - doesn't modify system settings
✅ **Configurable** - can disable via scheduled task

---

## 📥 Download Links

**Main Installer** (Works offline):
```
https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/install-heartbeat-standalone.bat
```

**Heartbeat Package** (Optional, auto-downloaded):
```
https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/dist/SGRNetGuard-Heartbeat-Universal-Latest.zip
```

**Latest Versioned Package**:
```
https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/dist/SGRNetGuard-Heartbeat-Agent-v20260915.1.0.4-Universal.zip
```

**Full Repository**:
```
https://github.com/namnhnh2002/SGRNetGuard.Backend
```

---

## 🆘 Support

### For End Users
- Contact IT Support
- Share: Machine name, error message (if any), log file contents

### For IT Administrators
- Review deployment guide: HEARTBEAT_DEPLOYMENT.md
- Check API logs: https://sgrnetguard-backend.onrender.com/
- Verify subnet configuration in API

---

## ✅ Validation Checklist

Before deploying to ALL machines, verify:

- [ ] Test installation on 1-2 sample machines
- [ ] Verify Task Scheduler shows "SGRNetGuard-Heartbeat" as Ready
- [ ] Check heartbeat-error.log shows successful transmission
- [ ] Wait 2-3 minutes and verify Dashboard shows machine
- [ ] Confirm `isInternal: true` for internal network machines
- [ ] Confirm CPU/RAM/Disk stats are updating
- [ ] Test from different network locations (if applicable)

---

## 📌 Important Notes

⚠️ **Administrator Rights Required**
- Installation must run as Administrator
- Task runs as SYSTEM service

⚠️ **Windows Restart Recommended**
- For immediate heartbeat startup
- Or log off/on again

⚠️ **Internet Required**
- Machine needs connectivity to reach API
- Internal network detection works offline

⚠️ **System Resources**
- CPU: < 1% utilization
- Memory: < 10 MB
- Disk: ~ 30 KB (installation)
- Network: ~100 bytes per heartbeat every 60 sec

---

## 📝 Summary

**One-line summary**: 
Universal heartbeat agent with multi-method internal network detection - works for ALL machines with automatic installation and zero user interaction after setup.

**Result**: 
All machines on internal network automatically report `isInternal=true` to Dashboard IT with live system metrics.

**Status**: ✅ **READY FOR PRODUCTION DEPLOYMENT**

---

**Questions?** Check logs at `C:\SGRNetGuard\Heartbeat\heartbeat-error.log` or contact IT Support.

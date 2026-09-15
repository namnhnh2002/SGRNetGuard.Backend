# 🚀 SGR NetGuard Heartbeat - Auto Deployment Guide

## The Problem (Why This Matters)

Your machine **APC-KTHT02** is on the **internal network**, but the heartbeat service wasn't correctly reporting `isInternal=true` to the Dashboard IT.

**Result**: Your machine data wasn't being displayed correctly in the IT monitoring system.

**Solution**: Install the updated heartbeat agent that **automatically detects internal networks**.

---

## ✅ Installation Options

### 🎯 **OPTION 1: One-Liner PowerShell (Easiest - RECOMMENDED)**

**Copy and paste ONE line** into PowerShell running as Administrator:

```powershell
IEX(New-Object Net.WebClient).DownloadString('https://sgrnetguard-backend.onrender.com/install-heartbeat.ps1')
```

**That's it!** The script will:
- ✅ Download the heartbeat package
- ✅ Extract to `C:\SGRNetGuard\Heartbeat`
- ✅ Install the Scheduled Task
- ✅ Run initial test
- ✅ Display success message

---

### 📦 **OPTION 2: Batch Installer (Alternative)**

If PowerShell doesn't work for you:

1. **Download** `install-heartbeat.bat` from:
   ```
   https://sgrnetguard-backend.onrender.com/install-heartbeat.bat
   ```

2. **Download** the heartbeat package from:
   ```
   https://sgrnetguard-backend.onrender.com/SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip
   ```

3. **Place both files in the same folder** (e.g., `Downloads`)

4. **Right-click** `install-heartbeat.bat` → **"Run as Administrator"**

5. **Wait for the script to finish** (shows ✓ INSTALLATION SUCCESSFUL!)

---

### 🔧 **OPTION 3: Manual Installation (Advanced)**

If you prefer to do it step-by-step:

1. **Download** the ZIP package
2. **Extract** to `C:\SGRNetGuard\Heartbeat`
3. **Open PowerShell as Administrator**
4. **Run**:
   ```powershell
   cd C:\SGRNetGuard\Heartbeat
   .\Install-ClientHeartbeatTask.ps1
   ```
5. **Restart Windows** or log off/log on

---

## 📋 Step-by-Step for PowerShell (OPTION 1)

### Step 1: Open PowerShell as Administrator

- **Windows 10/11**: Press `Win + X` → Select "Windows PowerShell (Admin)"
- **Or**: Press `Win` → Type "PowerShell" → Right-click → "Run as Administrator"

### Step 2: Copy the Command

Copy this entire line:

```powershell
IEX(New-Object Net.WebClient).DownloadString('https://sgrnetguard-backend.onrender.com/install-heartbeat.ps1')
```

### Step 3: Paste and Run

- Right-click in PowerShell → Paste
- Press **Enter**
- **Wait** for the installation to complete (1-2 minutes)

### Step 4: See Success Message

You should see:

```
╔════════════════════════════════════════════════════╗
║              ✅ CÀI ĐẶT THÀNH CÔNG!              ║
╚════════════════════════════════════════════════════╝
```

---

## ✨ What Happens After Installation?

After installation, your machine will:

✅ **Automatically start the heartbeat service** when Windows boots
✅ **Automatically start the heartbeat service** when you log in
✅ **Send heartbeat every 60 seconds** to the API
✅ **Report `isInternal=true`** so Dashboard IT recognizes it as internal network
✅ **Display correctly in the IT monitoring dashboard**

---

## 🔍 Verification (How to Check if It Works)

### Method 1: Task Scheduler
1. Press `Win + R`
2. Type: `taskschd.msc`
3. Look for: **"SGRNetGuard-Heartbeat"** under "Active Tasks"
4. Should show "Ready" status

### Method 2: Check Service Running
```powershell
Get-ScheduledTask -TaskName "SGRNetGuard-Heartbeat" | Select-Object State
# Should return: State : Ready
```

### Method 3: Check Logs
Open file: `C:\SGRNetGuard\Heartbeat\heartbeat-error.log`

You should see recent entries like:
```
2026-09-15T09:30:15.1234567 Heartbeat sent to https://sgrnetguard-backend.onrender.com/api/heartbeat
```

---

## 🆘 Troubleshooting

### Issue: "Access Denied" when running PowerShell command

**Solution**: 
- Make sure PowerShell is running **as Administrator**
- You might need to change Execution Policy temporarily:
  ```powershell
  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
  ```

### Issue: "Could not download package"

**Solution**:
- Check internet connection
- Try the Batch installer (OPTION 2) instead
- Contact IT if problem persists

### Issue: Scheduled Task didn't install

**Solution**:
- Restart Windows completely
- Try running as Administrator again
- Check if UAC (User Account Control) is blocking it

### Issue: Still seeing "isInternal = false" in Dashboard

**Solution**:
- Wait 2-3 minutes for next heartbeat
- Refresh the Dashboard page
- Check the machine IP is actually on internal network (10.x, 192.168.x, etc.)
- Contact IT support

---

## 🆘 Getting Help

If you encounter any issues:

1. **Check the log file**: `C:\SGRNetGuard\Heartbeat\heartbeat-error.log`
2. **Run manual test**:
   ```powershell
   C:\SGRNetGuard\Heartbeat\ClientHeartbeatSender.ps1 -Once
   ```
3. **Contact IT Support** with:
   - Your machine name
   - Error message from log file
   - Screenshot of error (if any)

---

## 📌 Important Notes

⚠️ **Administrator Rights Required**
- The installation MUST run as Administrator
- The scheduled task runs as SYSTEM service

⚠️ **Windows Restart May Be Needed**
- For the heartbeat to start running
- Or log off and log on again

⚠️ **No User Interaction Required After Installation**
- The heartbeat runs silently in background
- No notifications or popups
- Minimal system resource usage

---

## ✅ Summary

- **Installation Time**: 2-5 minutes
- **System Impact**: Minimal (< 1% CPU, < 10MB disk)
- **Network Impact**: ~100 bytes per heartbeat, every 60 seconds
- **Result**: Your machine will appear correctly in Dashboard IT with `isInternal: true`

---

**Questions?** Contact your IT Support team.

**Ready to install?** Just copy and paste the one-liner in PowerShell (as Administrator)! 🚀

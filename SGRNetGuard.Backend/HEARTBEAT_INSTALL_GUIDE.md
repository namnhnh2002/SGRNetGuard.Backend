# 🚀 SGR NetGuard Heartbeat - Installation Guide

## The Problem (Why This Matters)

Your machine is on the **internal network**, but the heartbeat service wasn't correctly reporting `isInternal=true` to the Dashboard IT.

**Solution**: Install the updated heartbeat agent that **automatically detects internal networks**.

---

## ✅ Installation Options

### 🎯 **OPTION 1: Standalone Batch Installer (EASIEST - RECOMMENDED)**

**No PowerShell, No Internet Required!**

1. **Download** `install-heartbeat-standalone.bat`
   ```
   https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/install-heartbeat-standalone.bat
   ```

2. **Run it**:
   - Right-click `install-heartbeat-standalone.bat`
   - Select **"Run as Administrator"**
   - Wait ~2 minutes

**Done!** Installation complete.

---

### 📦 **OPTION 2: With Pre-downloaded Package**

If you already have `SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip`:

1. Place both files in the **same folder**
2. Right-click `.bat` → "Run as Administrator"
3. Done!

---

### 🔧 **OPTION 3: Manual Installation (Advanced)**

1. Extract `SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip` to `C:\SGRNetGuard\Heartbeat`
2. Open PowerShell as Administrator
3. Run: `cd C:\SGRNetGuard\Heartbeat; .\Install-ClientHeartbeatTask.ps1`
4. Restart Windows

---

## 📋 Quick Start (OPTION 1)

### Step 1: Download
```
https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/install-heartbeat-standalone.bat
```

### Step 2: Run as Administrator
Right-click the file → "Run as Administrator" → Wait for success message

### Step 3: Restart
Restart Windows or log off/on

**That's it!** Heartbeat will run automatically.

---

## ✨ What Happens After Installation?

✅ Heartbeat starts automatically at Windows startup
✅ Heartbeat starts automatically when you log in
✅ Heartbeat is sent every 60 seconds
✅ Machine is marked as `isInternal: true`
✅ Appears correctly in IT Dashboard

---

## 🔍 Verification

### Check Task Scheduler
1. Press `Win + R` → Type `taskschd.msc`
2. Look for: **"SGRNetGuard-Heartbeat"**
3. Should show "Ready" status

### Check Logs
File: `C:\SGRNetGuard\Heartbeat\heartbeat-error.log`

Should show: `Heartbeat sent to ...`

---

## 🆘 Troubleshooting

| Problem | Solution |
|---------|----------|
| "Access Denied" | Right-click → "Run as Administrator" |
| Download failed | Place ZIP file in same folder or try again |
| Task not installed | Restart Windows and try again |
| Still showing external | Wait 2-3 minutes, then restart Windows |
| File not found | Make sure both files are in same folder or check filename |

---

## 📥 Download Links

**Installer**:
```
https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/install-heartbeat-standalone.bat
```

**Package** (optional):
```
https://github.com/namnhnh2002/SGRNetGuard.Backend/raw/main/SGRNetGuard.Backend/SGRNetGuard-Heartbeat-Agent-v20260915.1.0.3.zip
```

---

## 📌 Important Notes

⚠️ Run as Administrator - required for installation
⚠️ Restart Windows or log off/on for best results
⚠️ Runs silently in background - no notifications
⚠️ Uses minimal resources (< 1% CPU, < 10MB disk)

---

**Questions?** Contact IT Support.

**Ready?** Download and run the installer! 🚀

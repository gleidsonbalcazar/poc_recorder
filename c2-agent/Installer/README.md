# Paneas C2 Agent - MSI Installer

Multi-language Windows installer for Paneas C2 Agent using WiX Toolset 4.

## 📦 What's Included

This installer creates **3 separate MSI files**, one for each supported language:

- `PaneasC2Agent-1.0.0-pt-BR.msi` - Portuguese (Brazil) 🇧🇷
- `PaneasC2Agent-1.0.0-es-MX.msi` - Spanish (Mexico) 🇲🇽
- `PaneasC2Agent-1.0.0-en-US.msi` - English (US) 🇺🇸

Each MSI contains:
- Agent.exe (~34MB) - Main monitoring agent
- PaneasMonitorService.exe (~10MB) - Windows Service
- FFmpeg.exe (~100MB) - Video encoding
- appsettings.json - Configuration file

**Total MSI size per language:** ~150MB

---

## 🚀 Quick Start

### Prerequisites

- Windows 10/11 or Windows Server 2016+
- Administrator privileges
- .NET 8.0 Runtime (included in installer)

### Build Instructions

1. **Install WiX Toolset 4:**
   ```powershell
   dotnet tool install --global wix --version 4.0.5
   ```

2. **Build Agent and Service:**
   ```powershell
   cd c2-agent/Agent
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

   cd ../PaneasMonitorService
   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   ```

3. **Build all MSI installers:**
   ```powershell
   cd ../Installer/Scripts
   .\build-all-languages.ps1
   ```

Output will be in: `Installer/bin/Release/`

---

## 🔧 Installation

### Interactive Installation (GUI)

Double-click the MSI file for your language:
```
PaneasC2Agent-1.0.0-pt-BR.msi  (Brazilian Portuguese)
PaneasC2Agent-1.0.0-es-MX.msi  (Mexican Spanish)
PaneasC2Agent-1.0.0-en-US.msi  (English)
```

The installer will:
1. Show welcome screen
2. Display license agreement
3. **Ask for configuration** (C2 Server URL, Upload settings, Operation mode)
4. Choose installation directory
5. Install files, service, and task scheduler
6. Start the service automatically

### Silent Installation (Command-Line)

For automated deployment or GPO:

```powershell
# Install with default configuration
msiexec /i PaneasC2Agent-1.0.0-en-US.msi /qn

# Install with custom configuration
msiexec /i PaneasC2Agent-1.0.0-en-US.msi /qn ^
  C2_SERVER_URL="http://192.168.1.100:8000" ^
  UPLOAD_ENABLED="false" ^
  OPERATION_MODE="hybrid"
```

#### Available Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `C2_SERVER_URL` | `http://localhost:8000` | C2 server endpoint |
| `UPLOAD_ENABLED` | `false` | Enable automatic video uploads |
| `UPLOAD_ENDPOINT` | `` | HTTP upload server URL |
| `TUS_SERVER_URL` | `` | TUS upload server URL |
| `OPERATION_MODE` | `hybrid` | Mode: `hybrid`, `c2`, or `autonomous` |
| `INSTALLFOLDER` | `C:\Program Files\Paneas Monitor\Agent` | Installation directory |

---

## 📂 Installation Directories

The installer creates the following structure:

```
C:\Program Files\Paneas Monitor\Agent\
  ├─ bin\
  │  ├─ Agent.exe
  │  ├─ PaneasMonitorService.exe
  │  └─ appsettings.json
  └─ ffmpeg\
     └─ ffmpeg.exe

C:\ProgramData\C2Agent\
  ├─ bin\
  │  └─ appsettings.json (runtime config)
  ├─ Logs\
  └─ Videos\
```

---

## ⚙️ What Gets Installed

### 1. Windows Service
- **Name:** PaneasMonitorService
- **Startup:** Automatic
- **Account:** LocalSystem
- **Purpose:** Monitors Agent.exe and restarts if crashed

### 2. Scheduled Task
- **Name:** PaneasMonitorTask
- **Trigger:** At user logon
- **Run Level:** Highest
- **User:** INTERACTIVE (currently logged-in user)
- **Purpose:** Launches Agent.exe in user session for screen recording

### 3. Firewall Rule
- **Name:** Paneas Monitor - Media Server
- **Port:** 9000 (TCP)
- **Scope:** Local subnet
- **Purpose:** Allow HTTP media preview server

### 4. Registry Keys
- **Location:** `HKLM\Software\PaneasMonitor`
- **Values:** InstallLocation, Version, C2ServerUrl, OperationMode

### 5. Start Menu Shortcuts
- Uninstall Paneas C2 Agent
- Configuration Folder

---

## 🗑️ Uninstallation

### Via Control Panel
1. Open "Apps & Features" (Windows 10/11) or "Programs and Features" (Windows 7/8)
2. Find "Paneas C2 Agent"
3. Click "Uninstall"

### Via Command-Line
```powershell
# Silent uninstall
msiexec /x PaneasC2Agent-1.0.0-en-US.msi /qn

# Or by product code
msiexec /x {ProductCode} /qn
```

The uninstaller will:
- Stop and remove Windows Service
- Delete Scheduled Task
- Remove Agent and Service binaries
- Delete firewall rule
- Clean registry entries
- **Preserve** videos and logs by default (user can choose to delete)

---

## 🌐 Localization

The installer automatically detects Windows language and shows appropriate UI. However, the language is **determined by which MSI you run**, not by system locale.

### Supported Languages

| Language | Culture | LCID | Notes |
|----------|---------|------|-------|
| Portuguese (Brazil) | pt-BR | 1046 | Fully translated (WixUI built-in) |
| Spanish (Mexico) | es-MX | 2058 | Uses es-ES fallback |
| English (US) | en-US | 1033 | Default/reference language |

### Localized Components

- Installer dialogs (welcome, license, features, progress, finish)
- Button labels (Next, Back, Cancel, Install)
- Error messages (~300 system errors)
- Product name and descriptions
- Service display name and description
- Configuration dialog labels

---

## 🛠️ Build Script Options

The `build-all-languages.ps1` script supports several options:

```powershell
# Clean build (delete previous output)
.\build-all-languages.ps1 -Clean

# Skip binary build (use existing Agent.exe and Service.exe)
.\build-all-languages.ps1 -SkipBuild

# Debug build
.\build-all-languages.ps1 -Configuration Debug

# Combined
.\build-all-languages.ps1 -Clean -Configuration Release
```

---

## 🐛 Troubleshooting

### Build Errors

**Error:** `Agent.exe not found`
**Solution:** Build Agent project first:
```powershell
cd c2-agent/Agent
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

**Error:** `FFmpeg.exe not found`
**Solution:** Download FFmpeg and place in `c2-agent/Agent/ffmpeg/ffmpeg.exe`

**Error:** `WiX Toolset not found`
**Solution:** Install WiX:
```powershell
dotnet tool install --global wix --version 4.0.5
```

### Installation Errors

**Error:** `Service failed to start`
**Solution:** Check Event Viewer (Application log) for details. Common causes:
- Missing .NET 8.0 runtime (should be included in single-file)
- Insufficient permissions
- Port 9000 already in use

**Error:** `Scheduled task creation failed`
**Solution:** Ensure running as Administrator. Check Task Scheduler for existing task with same name.

**Error:** `Access denied`
**Solution:** Right-click MSI → "Run as Administrator"

### Runtime Issues

**Problem:** Agent not recording screen
**Solution:** Check if Agent.exe is running in user session:
```powershell
Get-Process Agent -IncludeUserName
```

**Problem:** Videos not uploading
**Solution:** Check `UPLOAD_ENABLED` setting in:
```
C:\ProgramData\C2Agent\bin\appsettings.json
```

**Problem:** Service keeps restarting
**Solution:** Check service logs at:
```
C:\ProgramData\C2Agent\Logs\
```

---

## 📝 Project Structure

```
Installer/
├─ Product.wxs                   # Main installer definition
├─ Files.wxs                     # File components
├─ ServiceInstall.wxs            # Windows Service installation
├─ TaskScheduler.wxs             # Scheduled Task creation
├─ PaneasMonitor.Installer.wixproj   # MSBuild project file
├─ License.rtf                   # EULA displayed during install
├─ Dialogs/
│  └─ ConfigurationDialog.wxs    # Custom configuration dialog
├─ Localization/
│  ├─ Product_pt-BR.wxl          # Portuguese strings
│  ├─ Product_es-MX.wxl          # Spanish strings
│  └─ Product_en-US.wxl          # English strings
├─ Scripts/
│  ├─ build-all-languages.ps1    # Build automation script
│  └─ sign-installers.ps1        # Code signing script (TODO)
└─ README.md                     # This file
```

---

## 🔐 Code Signing (TODO)

For production deployment, sign the MSI files:

```powershell
# Sign with certificate
signtool sign /f certificate.pfx /p password /t http://timestamp.digicert.com PaneasC2Agent-1.0.0-en-US.msi

# Verify signature
signtool verify /pa PaneasC2Agent-1.0.0-en-US.msi
```

---

## 📚 Additional Resources

- WiX Toolset: https://wixtoolset.org/
- WiX Documentation: https://wixtoolset.org/docs/v4/
- Paneas Monitor Docs: `../docs/deployment-installer-wix-gpo.md`

---

## 📄 License

See `License.rtf` for end-user license agreement.

This is POC/educational software. Not intended for production use without proper security review.

---

## 🤝 Support

For issues or questions:
- GitHub Issues: https://github.com/gleidsonbalcazar/poc_recorder/issues
- Documentation: `../docs/`

---

**Version:** 1.0.0
**Last Updated:** 2025-01-10
**WiX Version:** 4.0.5

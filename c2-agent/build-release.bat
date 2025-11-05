@echo off
echo ========================================
echo C2 Agent - Release Build Script
echo ========================================
echo.

REM Change to script directory
cd /d "%~dp0"

REM Clean previous builds
echo [1/5] Cleaning previous builds...
if exist "Agent\bin\Release" rd /s /q "Agent\bin\Release"
if exist "Agent\obj\Release" rd /s /q "Agent\obj\Release"
echo Done.
echo.

REM Build Release configuration
echo [2/5] Building Release configuration...
dotnet build Agent\Agent.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed!
    pause
    exit /b %ERRORLEVEL%
)
echo Done.
echo.

REM Publish single-file executable
echo [3/5] Publishing single-file executable...
dotnet publish Agent\Agent.csproj -c Release -r win-x64 --nologo
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Publish failed!
    pause
    exit /b %ERRORLEVEL%
)
echo Done.
echo.

REM Create releases directory
echo [4/5] Preparing releases directory...
if not exist "releases" mkdir releases
echo Done.
echo.

REM Copy executable to releases
echo [5/5] Copying executable to releases...
set SOURCE=Agent\bin\x64\Release\net10.0\win-x64\publish\Agent.exe
set DEST=releases\C2Agent-v1.0.0.exe

if exist "%SOURCE%" (
    copy /Y "%SOURCE%" "%DEST%" >nul
    echo Done.
    echo.

    REM Show file information
    echo ========================================
    echo BUILD SUCCESSFUL!
    echo ========================================
    echo.
    echo Executable location: %CD%\%DEST%
    echo.

    REM Get file size
    for %%I in ("%DEST%") do (
        set SIZE=%%~zI
        set /a SIZE_MB=%%~zI/1024/1024
    )
    echo File size: %SIZE_MB% MB
    echo.
    echo ========================================
    echo.
    echo Ready for distribution or installer creation!
    echo Next step: Run Inno Setup with installer.iss
    echo.
) else (
    echo ERROR: Executable not found at %SOURCE%
    echo Build may have failed.
    pause
    exit /b 1
)

pause

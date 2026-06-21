@echo off
setlocal enabledelayedexpansion

rem Run this from anywhere - it cd's to its own folder
cd /d "%~dp0"

echo.
echo === Publishing LazerGuanas Game Hunter (Release, win-x64, self-contained, single-file) ===
echo.

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo.
    echo [X] dotnet publish failed.
    exit /b 1
)

rem Locate Inno Setup compiler - checks per-user, machine-wide, then PATH
set "ISCC="
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do set "ISCC=%%I"
if not defined ISCC (
    echo.
    echo [X] ISCC.exe not found.
    echo     Install Inno Setup 6 from https://jrsoftware.org/isdl.php then re-run.
    exit /b 1
)

echo.
echo === Compiling installer with Inno Setup ===
echo Using: !ISCC!
echo.

"!ISCC!" GameTracker.iss
if errorlevel 1 (
    echo.
    echo [X] Inno Setup compile failed.
    exit /b 1
)

echo.
echo === Done ===
echo Installer:    %~dp0installer\LazerGuanas-Game-Hunter-Setup-1.0.0.exe
echo.
endlocal

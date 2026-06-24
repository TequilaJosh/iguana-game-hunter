@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem ============================================================
rem  Cut a release end-to-end:
rem    1. bump the version in GameTracker.csproj
rem    2. validate the build
rem    3. commit + push, then tag + push
rem  GitHub Actions then builds the installer .exe and publishes
rem  the GitHub Release.
rem
rem  Usage:
rem    release.bat            bump patch  (1.0.2 -> 1.0.3)
rem    release.bat patch      same as above
rem    release.bat minor      1.0.3 -> 1.1.0
rem    release.bat major      1.1.0 -> 2.0.0
rem    release.bat 1.4.2      set an explicit version
rem ============================================================

rem --- Read current <Version> from the .csproj ---
set "CURVER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "[regex]::Match((Get-Content 'GameTracker.csproj' -Raw),'<Version>(.*?)</Version>').Groups[1].Value"`) do set "CURVER=%%V"
if not defined CURVER (
    echo [X] Could not read ^<Version^> from GameTracker.csproj
    exit /b 1
)

rem --- Work out the new version ---
set "ARG=%~1"
if "%ARG%"=="" set "ARG=patch"

set "NEWVER="
echo(%ARG%| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul && set "NEWVER=%ARG%"

if not defined NEWVER (
    for /f "tokens=1,2,3 delims=." %%a in ("%CURVER%") do (
        set "MAJ=%%a"
        set "MIN=%%b"
        set "PAT=%%c"
    )
    if /i "%ARG%"=="major" (
        set /a MAJ+=1
        set "MIN=0"
        set "PAT=0"
    ) else if /i "%ARG%"=="minor" (
        set /a MIN+=1
        set "PAT=0"
    ) else if /i "%ARG%"=="patch" (
        set /a PAT+=1
    ) else (
        echo [X] Unknown argument "%ARG%".  Use: major ^| minor ^| patch ^| X.Y.Z
        exit /b 1
    )
    set "NEWVER=!MAJ!.!MIN!.!PAT!"
)

set "TAG=v!NEWVER!"

echo.
echo === Releasing !TAG!  (was v%CURVER%) ===
echo.

rem --- Must be a git repo ---
git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [X] Not a git repository.
    exit /b 1
)

rem --- Refuse if the tag already exists (pipe-free, reliable errorlevel) ---
git rev-parse -q --verify "refs/tags/!TAG!" >nul 2>&1 && (
    echo [X] Tag !TAG! already exists locally.  Pick a higher version.
    exit /b 1
)
set "REMOTE_HIT="
for /f "delims=" %%r in ('git ls-remote --tags origin "refs/tags/!TAG!" 2^>nul') do set "REMOTE_HIT=1"
if defined REMOTE_HIT (
    echo [X] Tag !TAG! already exists on the remote.  Pick a higher version.
    exit /b 1
)

rem --- Validate the build BEFORE touching anything ---
echo === Validating build ===
dotnet build -c Release -p:Version=!NEWVER! >nul
if errorlevel 1 (
    echo [X] Build failed - fix errors before releasing.
    exit /b 1
)
echo Build OK.
echo.

rem --- Write the new version into the .csproj ---
echo === Bumping version %CURVER% -^> !NEWVER! ===
powershell -NoProfile -Command "$p='GameTracker.csproj'; $c=[IO.File]::ReadAllText($p); $c=$c -replace '<Version>.*?</Version>','<Version>!NEWVER!</Version>'; $c=$c -replace '<AssemblyVersion>.*?</AssemblyVersion>','<AssemblyVersion>!NEWVER!.0</AssemblyVersion>'; $c=$c -replace '<FileVersion>.*?</FileVersion>','<FileVersion>!NEWVER!.0</FileVersion>'; [IO.File]::WriteAllText($p,$c)"
if errorlevel 1 (
    echo [X] Failed to update version in GameTracker.csproj
    exit /b 1
)

rem --- Commit everything pending (includes the version bump) ---
echo.
echo === Committing ===
git add -A
git commit -m "Release !TAG!"
if errorlevel 1 (
    echo [X] Commit failed.
    exit /b 1
)

rem --- Push branch + tag (the tag triggers the release workflow) ---
echo.
echo === Pushing to GitHub ===
git push
if errorlevel 1 (
    echo [X] git push failed.
    exit /b 1
)
git tag !TAG!
git push origin !TAG!
if errorlevel 1 (
    echo [X] Pushing tag failed.
    exit /b 1
)

echo.
echo === Done ===
echo Released !TAG!.  GitHub Actions is building the installer .exe and publishing the release.
echo Watch:    https://github.com/TequilaJosh/iguana-game-hunter/actions
echo Release:  https://github.com/TequilaJosh/iguana-game-hunter/releases
echo.
endlocal

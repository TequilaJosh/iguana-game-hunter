@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

rem ============================================================
rem  Cut a release: validates the build, commits any pending
rem  changes, then tags + pushes so GitHub Actions builds the
rem  installer and publishes the release.
rem
rem  To release a NEW version: bump <Version> in GameTracker.csproj
rem  first, then run this script.
rem ============================================================

rem --- Read <Version> from the .csproj ---
set "APPVER="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "[regex]::Match((Get-Content 'GameTracker.csproj' -Raw),'<Version>(.*?)</Version>').Groups[1].Value"`) do set "APPVER=%%V"
if not defined APPVER (
    echo [X] Could not read ^<Version^> from GameTracker.csproj
    exit /b 1
)
set "TAG=v%APPVER%"

echo.
echo === Releasing %TAG% ===
echo.

rem --- Make sure we're in a git repo with a remote ---
git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [X] Not a git repository.
    exit /b 1
)

rem --- Refuse to release if the tag already exists (bump the version) ---
git tag -l %TAG% | findstr /x %TAG% >nul
if not errorlevel 1 (
    echo [X] Tag %TAG% already exists.
    echo     Bump ^<Version^> in GameTracker.csproj before releasing.
    exit /b 1
)
git ls-remote --tags origin %TAG% 2>nul | findstr /e "%TAG%" >nul
if not errorlevel 1 (
    echo [X] Tag %TAG% already exists on the remote.
    echo     Bump ^<Version^> in GameTracker.csproj before releasing.
    exit /b 1
)

rem --- Validate it compiles before tagging ---
echo === Validating build ===
dotnet build -c Release -p:Version=%APPVER% >nul
if errorlevel 1 (
    echo [X] Build failed - fix errors before releasing.
    exit /b 1
)
echo Build OK.
echo.

rem --- Commit any pending changes ---
set "DIRTY="
for /f "delims=" %%S in ('git status --porcelain') do set "DIRTY=1"
if defined DIRTY (
    echo === Committing pending changes ===
    git add -A
    git commit -m "Release %TAG%"
    if errorlevel 1 (
        echo [X] Commit failed.
        exit /b 1
    )
) else (
    echo No pending changes to commit.
)

rem --- Push branch + tag (the tag triggers the release workflow) ---
echo.
echo === Pushing to GitHub ===
git push
if errorlevel 1 (
    echo [X] git push failed.
    exit /b 1
)
git tag %TAG%
git push origin %TAG%
if errorlevel 1 (
    echo [X] Pushing tag failed.
    exit /b 1
)

echo.
echo === Done ===
echo Pushed %TAG%. GitHub Actions is now building the installer and publishing the release.
echo Watch:    https://github.com/TequilaJosh/iguana-game-hunter/actions
echo Release:  https://github.com/TequilaJosh/iguana-game-hunter/releases
echo.
endlocal

@echo off
rem Launch the Tavern Tales Bot control panel + supervisor.
rem It keeps the bot running (auto-restarts on crash) and gives you a Restart button.
cd /d "%~dp0"
start "" http://localhost:8642
node supervisor.mjs

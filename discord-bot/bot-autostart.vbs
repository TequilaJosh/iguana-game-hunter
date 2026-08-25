' Launches the Tavern Tales bot control panel + supervisor with NO visible window.
' Used by the "TavernTalesBot" scheduled task (runs at logon). The supervisor
' single-instances itself, so running this when it's already up just exits.
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "C:\Users\Admin\Downloads\GameTracker\GameTracker\discord-bot"
sh.Run """C:\Program Files\nodejs\node.exe"" supervisor.mjs", 0, False

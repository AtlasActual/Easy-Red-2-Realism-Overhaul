# Release workflow

When the user asks to package a numbered release, also install the verified release DLL in their local Easy Red 2 game folder by default. This is authorized as part of packaging; do not ask for separate installation confirmation. Use package-only mode only when the user explicitly requests it.

The current game folder is `E:\SteamLibrary\steamapps\common\Easy Red 2`. The loaded plugin belongs at `BepInEx\plugins\ER2RealismOverhaul\ER2RealismOverhaul.dll`. `scripts/Build-Release.ps1` installs by default when `-PackageOnly` is omitted.

Preserve existing settings and other mods. Back up an installed DLL before replacing it, update its companion changelog, and verify the installed version and SHA256 against the packaged DLL. Do not replace a DLL while the game is running; report that installation is pending until the game closes.

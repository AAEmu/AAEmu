ArcheAge 1.2 (r208022) - AAEmu optimized client

1. Extract the entire ZIP into a writable folder on your game drive.
   Use 7-Zip if Windows Explorer cannot extract a ZIP this large.
2. If AAEmuLauncher.exe is included, double-click it. Browse to this extracted
   folder's bin32\archeage.exe, then set the server address and account details.
   This is the supplied GUI launcher adapted for the optimized client.
3. Alternatively, edit server.json (default: 127.0.0.1:1237, language: en_us)
   and run Play.cmd. Password input is masked and is not saved by this launcher.

Sound and music are enabled. The window uses your normal game settings.
Fast logo startup, 64 KB pack read-ahead and smooth CPU calibration are enabled.
This player runtime has no MCP server, command pipe, screenshot capture,
input automation, hidden-window handling or audio muting policy.

Keep game.aacm and every .aacb file together in this folder. They replace the
original game_pak with a lossless compressed byte stream. All game content,
including audio, remains intact. Do not rename these files or delete a blob.
Start through AAEmuLauncher.exe or Play.cmd so the compressed reader loads first.

Compression saves about 48% of pack storage. Actual loading speed depends on
your disk and CPU; the compressed pack adds decompression work.
This ZIP includes the client, not an AAEmu server. The server must be running.
Windows 64-bit is required for the bundled launcher; the game itself is x86.

Launcher/runtime source is in AAEmu-tools-source.zip; see LICENSE-AAEmu.txt.

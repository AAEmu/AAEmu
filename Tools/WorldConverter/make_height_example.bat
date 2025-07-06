@ECHO OFF
SET WorldConvert="AAEmu.WorldConverter.exe"
SET SourcePath="D:\Games\ArcheAge\AA 1.2 - Trion - r208022 - 2014-10-14 - NoHS - Unpacked\game\worlds"
SET DestPath="D:\Dev\ArcheAge\Maps\Dump AA 1.2 - Trion - r208022 - 2014-10-14\worlds"
ECHO ---------------------------
ECHO  Generate heightmap script
ECHO ---------------------------
ECHO From: %SourcePath%
ECHO To:   %DestPath%
ECHO ---------------------------
ECHO The contents of the destination folder will be wiped if you continue!
ECHO Press ENTER to continue or Ctrl+C to stop.
PAUSE
RMDIR /S /Q %DestPath%
MKDIR %DestPath%
%WorldConvert% -in %SourcePath% -out %DestPath% -export gimp
ECHO ---------------------------
ECHO  Done !
ECHO ---------------------------
PAUSE
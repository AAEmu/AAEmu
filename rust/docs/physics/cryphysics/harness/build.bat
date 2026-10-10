@call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul 2>nul
@cd /d D:\aa-re\cryphysics\harness
cl /nologo /Zi /Od %* 

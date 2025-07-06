This is a command-line tool that was used to generate pre-generated heightmap data for use with [AAEmu](https://github.com/AAEmu/AAEmu).
It can also export to raw data for that can be opened in GIMP or importing as a heightmap in Unity3D. 

Example:

`AAEmu.WorldConverter -in "C:\PathToUnpackedAA\game\worlds" -out "C:\PathToGenerateFiles"`

Command-line arguments:

| argument                      | help                                                                                                                                                                                                                                                |
|-------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **-in** &lt;inputfolder&gt;   | Specifies the source directory where to start searching for world files. This needs to be from the unpacked game_pak client files and point to the worlds folder within.                                                                            |
| **-out** &lt;outputfolder&gt; | Specifies the directory where to create the generate files.                                                                                                                                                                                         |
| **-export** &lt;option&gt;    | Create additional heightmap files. Possible options are:                                                                                                                                                                                            |
|                               | **gimp**    creates raw .data that can be opened as a 16-bit grayscale LittleEndian file                                                                                                                                                            |
|                               | **unity**   creates .raw data that can be used as a heightmap in Unity                                                                                                                                                                              |
| **-split** &lt;size&gt;       | Splits result of -export in blocks of cells instead of a single map (512x512 pixels). If you use import in unity, you can import them with settings of 1024x1024 size and 1024 max height for import of each cell (space them by 1024 on X/Z axis). |
| **-ver** &lt;type&gt;         | Changes which technique version is used to generate the heightmap data. Check source code for how they affect the outcome.                                                                                                                          |

You can also check out the included `make_height_example.bat` file where you can simply specify an input and output folder in its code and just run that. 
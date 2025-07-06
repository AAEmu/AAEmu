using System.Xml.Serialization;
using AAEmu.WorldConverter.Models;
using Newtonsoft.Json;

/*
 * AAEmu.WorldConverter
 * Tool that was used to generate pre-generated heightmap data for use with AAEmu.
 * Original Authors: AANut, NLObP, AAGene, HexLulz, Nikes, Igor, ZeromusXYZ, Alex
 * Tool was mostly used from 2019 to 2022
 */

namespace AAEmu.WorldConverter
{
    public enum VersionCalc
    {
        V1 = 1,
        V2 = 2,
        Draft = 3
    }

    public enum Export
    {
        None,
        Gimp,
        Unity
    }

    public class Program
    {
        const int CELL_SIZE = 512;
        const int SECTOR_SIZE = 32;
        const int SECTORS_IN_CELL = CELL_SIZE / SECTOR_SIZE;

        static void Main(string[] args)
        {
            /*
            
            Command-Line argument of the old example:
            -in "F:\[ArcheAge] Clients\AA 3.5.0.3 - Trion - r342464\game\worlds" -out "D:\ArcheAgeDev\AAEmu3503_Genesis\AAEmu.Game\Data\Worlds_35" -export gimp

            */
            var input = string.Empty;
            var output = string.Empty;
            var version = VersionCalc.Draft;
            var export = Export.None;
            var splitSize = 0;
            bool showHelp = false;
            for (var c = 0; c < args.Length; c++)
            {
                var a = args[c].ToLower();
                var p = string.Empty;
                if (c < args.Length - 1)
                    p = args[c+1];

                if ((a == "-in") || (a == "-i"))
                    input = p;
                if ((a == "-out") || (a == "-o"))
                    output = p;
                if ((a == "-export") || (a == "-e"))
                {
                    switch (p.ToLower())
                    {
                        case "gimp":
                            export = Export.Gimp;
                            break;
                        case "unity":
                            export = Export.Unity;
                            break;
                        default:
                            export = Export.None;
                            break;
                    }
                }
                if ((a == "-ver") || (a == "-version") || (a == "-v"))
                {
                    switch (p.ToLower())
                    {
                        case "1":
                        case "v1":
                            version = VersionCalc.V1;
                            break;
                        case "2":
                        case "v2":
                            version = VersionCalc.V2;
                            break;
                        default:
                            version = VersionCalc.Draft;
                            break;
                    }
                }
                if (a == "-split")
                {
                    splitSize = 512;
                }
                if ((a == "-help") || (a == "-?") || (a == "/?"))
                {
                    showHelp = true;
                }
            }

            if ((input == string.Empty) || (output == string.Empty) || (showHelp))
            {
                Console.WriteLine("WorldConverter tool to generate heightmaps for use in AAEmu");
                Console.WriteLine();
                Console.WriteLine(Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location) + @" -in ""C:\PathToUnpackedAA\game\worlds"" -out ""C:\PathToGenerateFiles""");
                //Console.WriteLine(Path.GetFileNameWithoutExtension(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + @" -in ""C:\PathToUnpackedAA\game\worlds"" -out ""C:\PathToGenerateFiles""");
                Console.WriteLine();
                Console.WriteLine("\t-in\t\tSpecifies the source directory where to start searching for world files.");
                Console.WriteLine("\t\t\tThis needs to be from the unpacked game_pak client files and point to the worlds folder within.");
                Console.WriteLine();
                Console.WriteLine("\t-out\t\tSpecifies the directory where to create the generate files.");
                Console.WriteLine();
                Console.WriteLine("\t-export\t\tCreate additional heightmap files. Possible options are:");
                Console.WriteLine("\t\t\tgimp\tcreates raw .data that can be opened as a 16-bit grayscale LittleEndian file");
                Console.WriteLine("\t\t\tunity\tcreates .raw data that can be used as a heightmap in Unity");
                Console.WriteLine("\t\t\t");
                Console.WriteLine("\t-split\t\tSplits result of -export in blocks of cells instead of a single map (512x512 pixels).");
                Console.WriteLine("\t\t\tif you use import in unity, you can import them with settings of 1024x1024 size,");
                Console.WriteLine("\t\t\tand 1024 max height for import of each cell (space them by 1024 on X/Z axis).");
                Console.WriteLine("\t\t\t");
                Console.WriteLine("\t-ver\t\tChanges which technique version is used to generate the heightmap data.");
                Console.WriteLine("\t\t\tCheck source code for how they affect the outcome.");
                Console.WriteLine("\t\t\t");
                return;
            }

            if (!Directory.Exists(input))
            {
                Console.WriteLine("Input folder not found {0}",input);
                return;
            }

            var files = new List<string>();
            SearchFiles(input, files);

            if (files.Count <= 0)
            {
                Console.WriteLine("Nothing to do for source {0}",input);
                return;
            }

            files.Reverse();


            var worlds = new List<JWorld>();

            var worldSerializer = new XmlSerializer(typeof(World));
            foreach (var file in files)
            {
                var jWorld = new JWorld();
                using (var stream = File.OpenRead(file))
                {
                    var world = (World)worldSerializer.Deserialize(stream);

                    var zones = new List<Zone>();

                    foreach (var worldZone in world.ZoneList)
                    {
                        var cells = new List<Cell>();

                        foreach (var worldZoneCell in worldZone.cellList)
                        {
                            var sectors = worldZoneCell.sectorList
                                .Select(worldZoneCellSector =>
                                    new Sector { X = worldZoneCellSector.x, Y = worldZoneCellSector.y }
                                )
                                .ToList();

                            cells.Add(new Cell
                            {
                                Sectors = sectors,
                                X = worldZoneCell.x,
                                Y = worldZoneCell.y
                            });
                        }

                        zones.Add(new Zone
                        {
                            Id = worldZone.id,
                            Name = worldZone.name,
                            Cells = cells
                        });
                    }

                    jWorld.Id = 0;
                    jWorld.Name = world.name;
                    // jWorld.OffsetX = world.name == "main_world" ? -14336f : 0f;
                    // jWorld.OffsetY = world.name == "main_world" ? -3072f : 0f;
                    jWorld.MaxHeight = world.maxTerrainHeight; // usually 4096
                    jWorld.HeightMaxCoeff = ushort.MaxValue / (jWorld.MaxHeight / 2.0); // was 4.0
                    jWorld.OceanLevel = world.oceanLevel;
                    jWorld.CellX = world.cellXCount;
                    jWorld.CellY = world.cellYCount;
                    jWorld.Zones = zones;
                    // jWorld.HeightMap = new ushort[jWorld.CellX * CELL_SIZE, jWorld.CellY * CELL_SIZE];
                    jWorld.HeightMap = new Hmap[jWorld.CellX, jWorld.CellY];
                    jWorld.HeightMapRaw = new ushort[jWorld.CellX * CELL_SIZE, jWorld.CellY * CELL_SIZE];

                    jWorld.HeighestPoint = 0;
                }

                Console.WriteLine($"[{jWorld.Name}] Parse xml done.");

                for (var cellX = 0; cellX < jWorld.CellX; cellX++)
                    for (var cellY = 0; cellY < jWorld.CellY; cellY++)
                    {
                        var heightMap =
                            $@"{input}/{jWorld.Name}/cells/{cellX:D3}_{cellY:D3}/client/terrain/heightmap.dat";
                        if (!File.Exists(heightMap))
                            continue;

                        var hmap = jWorld.HeightMap[cellX, cellY] = new Hmap();

                        using (var br = new BinaryReader(File.OpenRead(heightMap)))
                            hmap.Read(br, version == VersionCalc.V1);

                        var nodes = hmap.Nodes
                            .OrderBy(cell => cell.BoxHeightmap.Min.X)
                            .ThenBy(cell => cell.BoxHeightmap.Min.Y)
                            .Where(x => x.pHMData.Length > 0)
                            .ToList();

                        for (ushort sectorX = 0; sectorX < SECTORS_IN_CELL; sectorX++) // 16x16 sectors / cell
                            for (ushort sectorY = 0; sectorY < SECTORS_IN_CELL; sectorY++)
                                for (ushort unitX = 0; unitX < SECTOR_SIZE; unitX++) // sector = 32x32 unit size
                                    for (ushort unitY = 0; unitY < SECTOR_SIZE; unitY++)
                                    {
                                        var node = nodes[sectorX * SECTORS_IN_CELL + sectorY];
                                        var oX = cellX * CELL_SIZE + sectorX * SECTOR_SIZE + unitX;
                                        var oY = cellY * CELL_SIZE + sectorY * SECTOR_SIZE + unitY;

                                        uint value;
                                        switch (version)
                                        {
                                            case VersionCalc.V1:
                                                {
                                                    var doubleValue = node.fRange * 100000d;
                                                    var rawValue = node.RawDataByIndex(unitX, unitY);



                                                    value = (uint)(
                                                        (doubleValue / 1.52604335620711f) * 
                                                        jWorld.HeightMaxCoeff /
                                                        ushort.MaxValue * 
                                                        (rawValue + node.BoxHeightmap.Min.Z) * 
                                                        jWorld.HeightMaxCoeff
                                                        );
                                                }
                                                break;
                                            case VersionCalc.V2:
                                                {
                                                    value = node.RawDataByIndex(unitX, unitY);
                                                    var height = node.RawDataToHeight(value);
                                                }
                                                break;
                                            case VersionCalc.Draft:
                                                {
                                                    var height = node.GetHeight(unitX, unitY);
                                                    value = (uint)(height * jWorld.HeightMaxCoeff);
                                                }
                                                break;
                                            default:
                                                throw new ArgumentOutOfRangeException();
                                        }

                                        if (value > 65535)
                                        {
                                            // Console.WriteLine($"Scaled height is out of bounds {oX},{oY} => raw:{value}");
                                            value = 65535; // cap it at max
                                        }
                                        if (value > jWorld.HeighestPoint)
                                            jWorld.HeighestPoint = value;
                                        jWorld.HeightMapRaw[oX, oY] = (ushort)value;
                                    }

                        Console.WriteLine($"[{jWorld.Name}] Done parse cell, X: {cellX:D3}, Y: {cellY:D3}, Ver: {hmap.Version}, Flag: {hmap.Flags:X2}-{hmap.Flags2:X2}, ZRatio:{hmap.HeightmapZRatio}");
                    }

                var realHeight = jWorld.HeighestPoint / jWorld.HeightMaxCoeff;
                Console.WriteLine($"[{jWorld.Name}] parse completed. HeighestPoint value: {jWorld.HeighestPoint} => {realHeight}");
                worlds.Add(jWorld);

                if (export != Export.None)
                {
                    var format = export == Export.Gimp ? "data" : export == Export.Unity ? "raw" : "none";

                    var startX = 0;
                    var sizeX = CELL_SIZE;
                    var startY = 0;
                    var sizeY = CELL_SIZE;
                    for (var yPos = 0; yPos < jWorld.CellY; yPos++)
                    for (var xPos = 0; xPos < jWorld.CellX; xPos++)
                    {
                        var splitName = string.Empty;
                        if (splitSize > 0)
                        {
                            splitName = $"({xPos:00}-{yPos:00})";
                            startX = CELL_SIZE * xPos;
                            sizeX = CELL_SIZE;
                            startY = CELL_SIZE * yPos;
                            sizeY = CELL_SIZE;
                        }
                        else
                        {
                            startX = 0;
                            sizeX = CELL_SIZE * jWorld.CellX;
                            startY = 0;
                            sizeY = CELL_SIZE * jWorld.CellY;
                            // mark pos as being "the last cell" for the loop
                            xPos = jWorld.CellX;
                            yPos = jWorld.CellY;
                        }

                        using (var fs = new FileStream($@"{output}\{jWorld.Name}{splitName}.hmap.{format}", FileMode.Create,
                                   FileAccess.ReadWrite))
                        {
                            using (var bw = new BinaryWriter(fs))
                            {
                                Console.Write($"[{jWorld.Name}] exporting heightmap data {jWorld.Name}{splitName} ... ");

                                if (export == Export.Gimp)
                                {
                                    for (var y = (startY + sizeY) - 1; y > startY - 1; y--)
                                    for (var x = startX; x < (startX + sizeX); x++)
                                        bw.Write(jWorld.HeightMapRaw[x, y]);
                                }
                                else if (export == Export.Unity)
                                {
                                    for (var y = startY; y < (startY + sizeY); y++)
                                    for (var x = startX; x < (startX + sizeX); x++)
                                        bw.Write(jWorld.HeightMapRaw[x, y]);
                                }

                                Console.WriteLine($"Done.");
                            }
                        }
                    }
                }


                Console.WriteLine($"[{jWorld.Name}][{jWorld.CellX * CELL_SIZE}][{jWorld.CellY * CELL_SIZE}] Done.");
            }

            var contents = JsonConvert.SerializeObject(worlds, Formatting.Indented);
            File.WriteAllText($@"{output}\worlds.json", contents);
            foreach (var world in worlds)
            {
                if (Directory.Exists($@"{output}\${world.Name}"))
                    Console.WriteLine("World {0} is exist...", world.Name);
                else
                {
                    Directory.CreateDirectory($@"{output}\{world.Name}");
                    contents = JsonConvert.SerializeObject(world.Zones);
                    File.WriteAllText($@"{output}\{world.Name}\zones.json", contents);

                    using (var hmapStream = File.OpenWrite($@"{output}\{world.Name}\hmap.dat"))
                    using (var bw = new BinaryWriter(hmapStream))
                    {
                        bw.Write(1); // version
                        bw.Write(world.CellX);
                        bw.Write(world.CellY);
                        bw.Write(world.HeightMaxCoeff);
                        bw.Write(world.HeightMap.Length);

                        for (var cellX = 0; cellX < world.CellX; cellX++)
                            for (var cellY = 0; cellY < world.CellY; cellY++)
                            {
                                var data = new ushort[CELL_SIZE, CELL_SIZE];

                                for (var sectorX = 0; sectorX < SECTORS_IN_CELL; sectorX++)
                                    for (var sectorY = 0; sectorY < SECTORS_IN_CELL; sectorY++)
                                    {
                                        var sectorPosX = cellX * CELL_SIZE + sectorX * SECTOR_SIZE;
                                        var sectorPosY = cellY * CELL_SIZE + sectorY * SECTOR_SIZE;
                                        for (var x = 0; x < SECTOR_SIZE; x++)
                                            for (var y = 0; y < SECTOR_SIZE; y++)
                                            {
                                                var sx = sectorPosX + x;
                                                var sy = sectorPosY + y;
                                                var value = world.HeightMapRaw[sx, sy];

                                                data[sectorX * SECTOR_SIZE + x, sectorY * SECTOR_SIZE + y] = value;
                                            }
                                    }

                                var empty = data.Cast<ushort>().All(x => x == 0);
                                if (!empty)
                                {
                                    bw.Write(false);
                                    for (var sectorX = 0; sectorX < SECTORS_IN_CELL; sectorX++)
                                        for (var sectorY = 0; sectorY < SECTORS_IN_CELL; sectorY++)
                                            for (var x = 0; x < SECTOR_SIZE; x++)
                                                for (var y = 0; y < SECTOR_SIZE; y++)
                                                    bw.Write(data[sectorX * SECTOR_SIZE + x, sectorY * SECTOR_SIZE + y]);
                                }
                                else
                                    bw.Write(true);
                            }

                    }

                    Console.WriteLine($"[{world.Name}] Done Write to output");
                }
            }
        }

        private static void SearchFiles(string path, List<string> files)
        {
            var directories = Directory.GetDirectories(path);
            foreach (var directory in directories)
            {
                var list = Directory.GetFiles(directory, "world.xml", SearchOption.TopDirectoryOnly);
                if (list.Length > 0)
                    files.AddRange(list);
            }
        }
    }
}

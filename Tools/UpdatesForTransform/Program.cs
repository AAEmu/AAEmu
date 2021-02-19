using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace UpdatesForTransform
{
    class Program
    {

        public static float ConvertSbyteDirectionToDegree(sbyte direction)
        {
            var angle = direction * (360f / 128);
            if (angle < 0)
                angle += 360;
            if (angle > 360)
                angle -= 360;
            return angle;
        }

        public static sbyte ConvertDegreeToSByteDirection(float degree)
        {
            if (degree < 0)
                degree = 360 + degree;
            var res = (sbyte)(degree / (360f / 128));
            if (res > 85)
                res = (sbyte)((degree - 360) / (360f / 128));
            return res;
        }


        static void ConvertNpcSpawnsFile(string fileName)
        {
            var oldFileName = Path.ChangeExtension(fileName, ".json.old");

            if (File.Exists(oldFileName))
            {
                Console.WriteLine("Already converted {0}", fileName);
                return;
            }

            var oldJsonData = File.ReadAllText(fileName);

            var oldSpawners = JsonConvert.DeserializeObject<List<NpcSpawnerOld>>(oldJsonData);

            var newSpawners = new List<NpcSpawnerNew>();
            if (oldSpawners != null)
            {

                foreach (var os in oldSpawners)
                {
                    var ns = new NpcSpawnerNew();

                    ns.Count = os.Count;
                    ns.Id = os.Id;
                    ns.UnitId = os.UnitId;
                    // Fix the zero-scale in the old files
                    if (os.Scale > 0f)
                        ns.Scale = os.Scale;
                    else
                        ns.Scale = 1f;
                    ns.Position.X = os.Position.X;
                    ns.Position.Y = os.Position.Y;
                    ns.Position.Z = os.Position.Z;
                    ns.Position.Yaw = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationZ));
                    ns.Position.Pitch = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationY));
                    ns.Position.Roll = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationX));
                    newSpawners.Add(ns);
                }
            }

            if (newSpawners.Count != oldSpawners.Count)
                throw new Exception(string.Format("Not everything was converted for file {0}",fileName));

            File.Move(fileName, oldFileName);

            var newJsonData = JsonConvert.SerializeObject(newSpawners,Formatting.Indented);
            File.WriteAllText(fileName, newJsonData);
            // File.WriteAllText(fileName + ".new", newJsonData);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("This is a helper tool to convert spawn files from the old sbyte X/Y/Z rotation to radians in Yaw/Pitch/Roll");
            Console.WriteLine("-----------------------------------------------------------------------------------------------------------");
            Console.WriteLine();

            var searchPath = Path.Combine(".", "Data");
            // var searchpath = Path.Combine("..", "..", "..", "..", "..", "AAEmu.Game", "Data");
            searchPath = Path.GetFullPath(searchPath);
            Console.WriteLine("Change Source Path ? (leave blank to keep default folder)");
            Console.Write("{0} : ",searchPath);
            var customPath = Console.ReadLine();
            if (!string.IsNullOrEmpty(customPath))
            {
                searchPath = Path.GetFullPath(customPath);
            }
            var di = new DirectoryInfo(searchPath);
            Console.WriteLine("Searching for files in {0}", searchPath);
            var npc_spawns_jsons = di.GetFiles("npc_spawns.json", SearchOption.AllDirectories).ToList();
            var doodad_spawns_jsons = di.GetFiles("doodad_spawns.json", SearchOption.AllDirectories).ToList();
            var transfer_spawns_jsons = di.GetFiles("transfer_spawns.json", SearchOption.AllDirectories).ToList();
            var allFiles = new List<FileInfo>();
            allFiles.AddRange(npc_spawns_jsons);
            allFiles.AddRange(doodad_spawns_jsons);
            allFiles.AddRange(transfer_spawns_jsons);
            Console.WriteLine("Found {0} files found ({1} NPC, {2} Doodad and {3} transfers).", allFiles.Count, npc_spawns_jsons.Count, doodad_spawns_jsons.Count, transfer_spawns_jsons.Count);

            Console.WriteLine();
            Console.Write("Continue ? (y/N): ");
            var confirm = Console.ReadLine()?.ToLower();
            if (confirm != "y")
            {
                Console.WriteLine("Aborted.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Converting files ...");
            foreach (var file in allFiles)
            {
                ConvertNpcSpawnsFile(file.FullName);
            }

            Console.WriteLine("Done ... ");
        }
    }
}

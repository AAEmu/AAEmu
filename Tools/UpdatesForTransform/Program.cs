using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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


        static void ConvertNpcSpawnsFile(string filename)
        {
            var oldfilename = Path.ChangeExtension(filename, ".json.old");

            if (File.Exists(oldfilename))
            {
                Console.WriteLine("Already converted {0}", filename);
                return;
            }

            var oldJsonData = File.ReadAllText(filename);

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
                    ns.Position.Yaw = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationX));
                    ns.Position.Pitch = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationY));
                    ns.Position.Roll = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationZ));
                    newSpawners.Add(ns);
                }
            }

            if (newSpawners.Count != oldSpawners.Count)
                throw new Exception(string.Format("Not everything was converted for file {0}",filename));

            // File.Move(filename, oldfilename);

            var newJsonData = JsonConvert.SerializeObject(newSpawners,Formatting.Indented);
            File.WriteAllText(filename+".new", newJsonData);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("This is a helper tool to convert spawn files from the old sbyte X/Y/Z rotation to radians in Yaw/Pitch/Roll");
            Console.WriteLine("-----------------------------------------------------------------------------------------------------------");
            Console.WriteLine();

            var searchpath = Path.Combine(".", "Data");
            // var searchpath = Path.Combine("..", "..", "..", "..", "..", "AAEmu.Game", "Data");
            searchpath = Path.GetFullPath(searchpath);
            var di = new DirectoryInfo(searchpath);
            Console.WriteLine("Searching for files in {0}", searchpath);
            var npc_spawns_jsons = di.GetFiles("npc_spawns.json", SearchOption.AllDirectories).ToList();
            /*
            Console.WriteLine();
            foreach (var file in npc_spawns_jsons)
            {
                Console.WriteLine(file.FullName);
            }
            Console.WriteLine();
            */
            Console.WriteLine("Found {0} files.",npc_spawns_jsons.Count);

            Console.WriteLine();
            Console.Write("Continue ? (y/N): ");
            var confirm = Console.ReadLine().ToLower();
            if (confirm != "y")
            {
                Console.WriteLine("Aborted.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Converting files ...");
            foreach (var file in npc_spawns_jsons)
            {
                ConvertNpcSpawnsFile(file.FullName);
            }

            Console.Write("Done ... ");
            Console.ReadLine();
        }
    }
}

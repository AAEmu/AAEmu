using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Numerics;

namespace UpdatesForTransform
{
    class Program
    {

        private static List<string> OldConvertedFiles = new List<string>();
        private static int OldEntriesFound = 0;
        private const float ToShortDivider = (1f / 32768f); // ~0.000030518509f ;
        private const float ToSByteDivider = (1f / 127f);   // ~0.007874015748f ;

        public static float ConvertSbyteDirectionToDegree(sbyte direction)
        {
            var angle = direction * (360f / 128);
            return angle % 360f;
        }

        public static sbyte ConvertDegreeToSByteDirection(float degree)
        {
            degree %= 360f;
            var res = (sbyte)(degree / (360f / 128));
            if (res > 85)
                res = (sbyte)((degree - 360) / (360f / 128));
            return res;
        }

        public static Vector3 FromQuaternion(float x, float y, float z, float w)
        {
            Vector3 angles;

            // roll (x-axis rotation)
            var sinRCosP = 2 * (w * x + y * z);
            var cosRCosP = 1 - 2 * (x * x + y * y);
            angles.X = MathF.Atan2(sinRCosP, cosRCosP);

            // pitch (y-axis rotation)
            var sinP = 2 * (w * y - z * x);
            angles.Y = MathF.Abs(sinP) >= 1 ? MathF.CopySign(MathF.PI / 2f, sinP) : MathF.Asin(sinP);

            // yaw (z-axis rotation)
            var sinYCosP = 2 * (w * z + x * y);
            var cosYCosP = 1 - 2 * (y * y + z * z);
            angles.Z = MathF.Atan2(sinYCosP, cosYCosP);

            return angles;
        }

        public static Vector3 FromQuaternion(Quaternion q)
        {
            return FromQuaternion(q.X, q.Y, q.Z, q.W);
        }

        static void ConvertNpcSpawnsFile(string fileName)
        {
            var oldFileName = Path.ChangeExtension(fileName, ".json.old");

            if (File.Exists(oldFileName))
            {
                Console.WriteLine("Already converted {0}", fileName);
                return;
            }

            var isQuatRotation = Path.GetFileNameWithoutExtension(fileName).ToLower().Contains("doodad");

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

                    if (!isQuatRotation)
                    {
                        ns.Position.Roll = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationX), 4, MidpointRounding.ToEven) % 360f;
                        ns.Position.Pitch = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationY), 4, MidpointRounding.ToEven) % 360f;
                        ns.Position.Yaw = MathF.Round(ConvertSbyteDirectionToDegree(os.Position.RotationZ), 4, MidpointRounding.ToEven) % 360f;
                    }
                    else
                    {
                        // Special consideration for doodads, as those where stored wrong originally
                        var y = (ConvertSbyteDirectionToDegree(os.Position.RotationX) % 360f) / 360f;
                        var x = (ConvertSbyteDirectionToDegree(os.Position.RotationY) % 360f) / 360f;
                        var z = (ConvertSbyteDirectionToDegree(os.Position.RotationZ) % 360f) / 360f;
                        var ww = 1f - ((x * x) + (y * y) + (z * z));
                        var w = MathF.Sqrt(ww);
                        var q = new Quaternion(x, y, z, w);
                        // q = Quaternion.Normalize(q);
                        var v = FromQuaternion(q);

                        ns.Position.Roll = MathF.Round(v.X / MathF.PI * 180f, 4, MidpointRounding.ToEven) % 360f;
                        ns.Position.Pitch = MathF.Round(v.Y / MathF.PI * 180f, 4, MidpointRounding.ToEven) % 360f;
                        ns.Position.Yaw = MathF.Round(v.Z / MathF.PI * 180f, 4, MidpointRounding.ToEven) % 360f;
                    }

                    ns.FuncGroupId = os.FuncGroupId;
                    newSpawners.Add(ns);

                    if ((os.Position.RotationX != 0f) || (os.Position.RotationY != 0f) || (os.Position.RotationZ != 0f))
                        OldEntriesFound++;
                }
            }

            if (newSpawners.Count != oldSpawners.Count)
                throw new Exception(string.Format("Not everything was converted for file {0}",fileName));

            File.Move(fileName, oldFileName);
            OldConvertedFiles.Add(oldFileName);

            var newJsonData = JsonConvert.SerializeObject(newSpawners,Formatting.Indented);
            File.WriteAllText(fileName, newJsonData);
            // File.WriteAllText(fileName + ".new", newJsonData);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("This is a helper tool to convert spawn files from the old sbyte X/Y/Z rotation to radians in Roll/Pitch/Yaw");
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
            Console.WriteLine();
            Console.WriteLine("Found {0} files found ({1} NPC, {2} Doodad and {3} transfers).", allFiles.Count, npc_spawns_jsons.Count, doodad_spawns_jsons.Count, transfer_spawns_jsons.Count);

            foreach(var f in allFiles)
            {
                if (File.Exists(f.FullName+".old"))
                    OldConvertedFiles.Add(f.FullName);
            }
            if (OldConvertedFiles.Count > 0)
            {
                Console.WriteLine("Also found {0} backup files from previous conversions", OldConvertedFiles.Count);
                Console.WriteLine();
                Console.Write("Do you want to revert the backup files first ? (Y/n): ");
                var confirmRestore = Console.ReadLine()?.ToLower();
                if ((confirmRestore == "y") || (confirmRestore == string.Empty))
                {
                    Console.WriteLine("Restoring previous backup files ...");
                    var restored = 0;
                    foreach (var newFName in OldConvertedFiles)
                        try
                        {
                            var backupName = newFName + ".old";
                            File.Delete(newFName);
                            File.Move(backupName, newFName);
                            restored++;
                        }
                        catch
                        {
                            Console.WriteLine("Failed to restore: {0}", newFName);
                        }

                    Console.WriteLine("Restored {0} files !", restored);
                    Console.WriteLine();
                }
            }

            Console.WriteLine();
            Console.Write("Continue with converting ? (y/N): ");
            var confirm = Console.ReadLine()?.ToLower();
            if (confirm != "y")
            {
                Console.WriteLine("Aborted.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Converting files ...");
            OldEntriesFound = 0;
            OldConvertedFiles.Clear();
            foreach (var file in allFiles)
            {
                ConvertNpcSpawnsFile(file.FullName);
            }
            Console.WriteLine("Done ... ");
            Console.WriteLine("");
            if (OldEntriesFound <= 0)
            {
                Console.WriteLine("+----------------------------------------------------------------+");
                Console.WriteLine("| It looks like the json files were already converted previously |");
                Console.WriteLine("| as no old rotation data was found.                             |");
                Console.WriteLine("| It is HIGHLY ADVISED to RESTORE the created .json.old files !  |");
                Console.WriteLine("| You will need to do this manually.                             |");
                Console.WriteLine("+----------------------------------------------------------------+");
                Console.WriteLine("");
                System.Threading.Thread.Sleep(1500);
            }
            Console.Write("Do you want to delete the {0} old files ? (y/N) : ",OldConvertedFiles.Count);
            var doDelete = Console.ReadLine();
            var deleted = 0;
            if (doDelete.ToLower().StartsWith("y"))
            {
                foreach (var oldFName in OldConvertedFiles)
                    try
                    {
                        File.Delete(oldFName);
                        deleted++;
                    }
                    catch 
                    {
                        Console.WriteLine("Failed to delete: {0}", oldFName);
                    }
            }
            Console.WriteLine("Deleted {0} old files.", deleted);
        }
    }
}

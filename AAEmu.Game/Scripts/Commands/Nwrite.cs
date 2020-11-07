using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace AAEmu.Game.Scripts.Commands
{
    public class Nwrite : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            string[] name = { "nwrite", "nw"};
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target)";
        }

        public string GetCommandHelpText()
        {
            return "write's npc's current position / rotation to json";
        }

        public void Execute(Character character, string[] args)
        {
            if (character.CurrentTarget != null && character.CurrentTarget != character)
            {
                if (character.CurrentTarget is Npc npc) {
                    var worlds = WorldManager.Instance.GetWorlds();
                    foreach (var world in worlds)
                    {
                        if (npc.Spawner.Position.WorldId == world.Id)
                        {
                            string path = ($"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");

                            var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");
                            if (string.IsNullOrWhiteSpace(contents))
                                _log.Warn(
                                    $"File {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json doesn't exists or is empty.");
                            else
                            {
                                if (JsonHelper.TryDeserializeObject(contents, out List<JsonNpcSpawns> spawners, out _))
                                {
                                    foreach (var spawner in spawners)
                                    {
                                        var pos = npc.Position;

                                        if (spawner.Id == npc.Spawner.Id)
                                        {
                                            spawner.Position.X = pos.X;
                                            spawner.Position.Y = pos.Y;
                                            spawner.Position.Z = pos.Z;
                                            spawner.Position.RotationZ = pos.RotationZ;
                                            break;
                                        }
                                    }

                                    string json = JsonConvert.SerializeObject(spawners.ToArray(), Formatting.Indented);
                                    File.WriteAllText(path, json);
                                    character.SendMessage("[Nwrite] ObjId: {0} has been saved!", character.CurrentTarget.ObjId);
                                }
                                else
                                    throw new Exception($"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json file");
                            }
                        }
                    }
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Commons.IO;
using Newtonsoft.Json;
using NLog;

namespace AAEmu.Game.Scripts.Commands
{
    public class Teleport : ICommand
    {
        private readonly Logger _log = LogManager.GetCurrentClassLogger();

        public const bool AllowPingPos = true; // Enable or Disable /teleport . (dot) command functionality

        private List<TPLoc> _locations = new List<TPLoc>();
        private string _locationList;

        public void OnLoad()
        {
            CommandManager.Instance.Register("teleport", this);
            InitTeleports();
        }

        public string GetCommandLineHelp()
        {
            return "[location]";
        }

        public string GetCommandHelpText()
        {
            if (AllowPingPos)
                return "Teleports you to target location. if no [location] is provided, "
                     + "you will get a list of available names. You can also use a period "
                     + "(.) as a location name to teleport to a location you marked on the map.";
            /*else
                return "Teleports you to target location. if no [location] is provided, you "
                     + "will get a list of available names."; */
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 1) 
            {
                var n = args[0].ToLower();

                if (AllowPingPos && (n == "."))
                {
                    var localPos = character.LocalPingPosition;
                    if ((localPos.X == 0.0f) && (localPos.Y == 0.0f))
                    {
                        character.SendMessage("|cFFFFFF00[Teleport] Make sure you marked a location on the map WHILE "
                                            + "IN A PARTY OR RAID before using this teleport function.\n"
                                            + "See also, the /soloparty command to make a solo party.|r");
                    }
                    else
                    {
                        var height = WorldManager.Instance.GetHeight(character.Transform.ZoneId, localPos.X, localPos.Y);
                        if (height == 0f)
                        {
                            character.SendMessage("|cFFFF0000[Teleport] Target height was |cFFFFFFFFzero|cFFFF0000. "
                                + "You likely tried to teleport out of bounds, or no heightmaps where loaded on the server.\n"
                                + "If you still want to move to the target location, you can use "
                                + "|cFFFFFFFF/move|cFFFF0000 to go to the following location|cFF40FF40\n"
                                + $"X:{localPos.X.ToString("0.0")} Y:{localPos.Y.ToString("0.0")}|r");
                        }
                        else
                        {
                            height += 2.5f; // Compensate a bit for terrain irregularities
                            character.SendMessage($"Teleporting to |cFFFFFFFFX:{localPos.X} Y:{localPos.Y} Z:{height}|r");
                            character.ForceDismount();
                            character.DisabledSetPosition = true;
                            character.SendPacket(new SCTeleportUnitPacket(0, 0, localPos.X, localPos.Y, height, 0));
                        }
                    }
                }
                else if (character.InstanceId != WorldManager.DefaultInstanceId)
                {
                    character.SendMessage("|cFFFFFF00[Teleport] Named teleports are not allowed inside a instance.|r");
                }
                else
                {
                    bool foundIt = false;
                    foreach (TPLoc item in _locations)
                    {
                        if (item.Name == n)
                        {
                            foundIt = true;
                        }
                        else if (item.AltNames != null)
                        {
                            foreach (string alt in item.AltNames)
                            {
                                if (alt == n)
                                {
                                    foundIt = true;
                                    break;
                                }
                            }
                        }
                        if (foundIt)
                        {
                            character.SendMessage($"[Teleport] Teleporting to |cFFFFFFFF{item.Info}|r");
                            character.ForceDismount();
                            character.DisabledSetPosition = true;
                            character.SendPacket(new SCTeleportUnitPacket(0, 0, item.X, item.Y, item.Z, 0));
                            break;
                        }
                    }
                    if (!foundIt)
                    {
                        character.SendMessage($"|cFFFF0000[Teleport] Unavailable Location [{args[0]}]|r");
                    }
                }
                return;
            }

            if (AllowPingPos)
                character.SendMessage($"Usage : {CommandManager.CommandPrefix}teleport <Location>\n"
                                    + "Use a period (.) to teleport to YOUR marked location on the map, "
                                    + "or use one of the following locations :");
            /*else
                character.SendMessage($"Usage : {CommandManager.CommandPrefix}teleport <Location>\n"
                                    + "Teleport to one of the following locations :"); */
            character.SendMessage(_locationList);
        }

        private void InitTeleports()
        {
            TPLocList json = new TPLocList();
            try
            {
                var filePath = Path.Combine(FileManager.AppPath, "Scripts", "Commands", "teleports.json");
                var contents = FileManager.GetFileContents(filePath);
                if (string.IsNullOrWhiteSpace(contents))
                    throw new IOException($"File {filePath} doesn't exists or is empty.");
                json = JsonConvert.DeserializeObject<TPLocList>(contents);
                _locations.Clear();
                _locations.AddRange(json.tplocs);
                _locations.Sort();

                // Order teleports by region nicely
                var sb = new StringBuilder();
                foreach (TeleportCommandRegions r in Enum.GetValues(typeof(TeleportCommandRegions)))
                    sb.Append($"|cFFFFFFFF{r.ToString()}|r: {string.Join(' ', _locations.Where((tp) => tp.Region == r))}\n");
                _locationList = sb.ToString();
            }
            catch (Exception x)
            {
                _log.Error("Exception: " + x.Message);
            }
        }
    }

    public enum TeleportCommandRegions { East = 0, West = 1, Auroria = 2, Dungeons = 3, Other = 4 }

    public class TPLoc
    {
        [JsonProperty("Name")]
        public string Name { get; set; }
        [JsonProperty("Info")]
        public string Info { get; set; }
        [JsonProperty("X")]
        public int X { get; set; }
        [JsonProperty("Y")]
        public int Y { get; set; }
        [JsonProperty("Z")]
        public int Z { get; set; }
        [JsonProperty("AltNames")]
        public string[] AltNames { get; set; }
        [JsonProperty("Region")]
        public TeleportCommandRegions Region { get; set; }
    }

    public class TPLocList
    {
        [JsonProperty("Teleports")]
        public List<TPLoc> tplocs = new List<TPLoc>();
    }
}

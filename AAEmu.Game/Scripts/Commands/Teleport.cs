using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Packets.G2C;
using System.Collections.Generic;

namespace AAEmu.Game.Scripts.Commands
{
    public class Teleport : ICommand
    {
        public List<TPloc> locations = new List<TPloc>();

        public void OnLoad()
        {
            CommandManager.Instance.Register("teleport", this);
 
            loadLocations();
        }

        public string GetCommandLineHelp()
        {
            return "[location]";
        }

        public string GetCommandHelpText()
        {
            return "Teleports you to target location. if no [location] is provided, you will get a list of available names";
        }

        public void loadLocations(){
            // West
            // locations.Add(new TPloc { Name = "aubrecradle", Info = "Aubre Cradle, Ironwrought", X = 7664, Y = 12957, Z = 400 });//228,71
            locations.Add(new TPloc { Name = "cinderstone", Info = "Cinderstone Moor, Seachild Wharf", X = 15400, Y = 11543, Z = 107, altNames = new string[] { "cinder" } });
            locations.Add(new TPloc { Name = "dewstone", Info = "Dewstone Plains, Sandcloud", X = 12204, Y = 13305, Z = 178, altNames = new string[] { "dew" } });
            locations.Add(new TPloc { Name = "gweonid", Info = "Gweonid Forest, Memoria", X = 10604, Y = 14925, Z = 280, altNames = new string[] { "gwen","elf"}
});
            locations.Add(new TPloc { Name = "halcyona", Info = "Halcyona, Suns End", X = 9342, Y = 10321, Z = 187, altNames = new string[] { "halcy" } });
            locations.Add(new TPloc { Name = "hellswamp", Info = "Hellswamp, Anarchi", X = 7482, Y = 9853, Z = 189, altNames = new string[] { "hell" } });
            locations.Add(new TPloc { Name = "karkasse", Info = "Karkasse Ridgelands, Lavis", X = 10470, Y = 17346, Z = 186, altNames = new string[] { "kar","karkase"} });
            locations.Add(new TPloc { Name = "lilyut", Info = "Lilyut Hills, Windshade", X = 13502, Y = 15035, Z = 210, altNames = new string[] { "lily","lili"} });
            locations.Add(new TPloc { Name = "marianople", Info = "Marianople, Marianople", X = 11144, Y = 12066, Z = 145, altNames = new string[] { "maria","west"} });
            locations.Add(new TPloc { Name = "sanddeep", Info = "Sanddeep, Golden Fable Harbor", X = 10720, Y = 9591, Z = 105, altNames = new string[] { "sand" } });
            locations.Add(new TPloc { Name = "solzreed", Info = "Solzreed Peninsula, Cresent Throne", X = 15369, Y = 13864, Z = 159, altNames = new string[] { "solz","nuian"} });
            locations.Add(new TPloc { Name = "twocrowns", Info = "Two Crowns, Ezna", X = 13500, Y = 10531, Z = 223, altNames = new string[] { "2c","twoc"} });
            locations.Add(new TPloc { Name = "whitearden", Info = "White Arden, Birchkeep", X = 9682, Y = 12775, Z = 161, altNames = new string[] { "arden" } });
            
            // East
            locations.Add(new TPloc { Name = "arcumiris", Info = "Arcum Iris, Parchsun Settlement", X = 20509, Y = 7173, Z = 193, altNames = new string[] { "arcum","harani"} });
            locations.Add(new TPloc { Name = "falcorth", Info = "Falcorth Plains, Oxion Clan", X = 23818, Y = 9102, Z = 589, altNames = new string[] { "firran" } });
            locations.Add(new TPloc { Name = "hasla", Info = "Hasla, Veroe", X = 30029, Y = 8760, Z = 539 });
            locations.Add(new TPloc { Name = "mahadevi", Info = "Mahadevi, City of Towers", X = 19284, Y = 8620, Z = 227, altNames = new string[] { "maha" } });
            locations.Add(new TPloc { Name = "perinor", Info = "Perinor Ruins, Stonehew", X = 27787, Y = 6732, Z = 572, altNames = new string[] { "peri" } });
            locations.Add(new TPloc { Name = "rookborne", Info = "Rookborne Basin, Watermist", X = 25388, Y = 9753, Z = 714, altNames = new string[] { "rook","rookborn"} });
            locations.Add(new TPloc { Name = "silentforest", Info = "Silent Forest, Count Sebastians Retreat", X = 23306, Y = 12526, Z = 271, altNames = new string[] { "sf","silent"} });
            locations.Add(new TPloc { Name = "solis", Info = "Solis Headlands, Austera", X = 16743, Y = 9018, Z = 120, altNames = new string[] { "austera","east"} });
            locations.Add(new TPloc { Name = "tigerspine", Info = "Tigerspine Mountains, Anvilton", X = 21772, Y = 8089, Z = 404, altNames = new string[] { "tiger" } });
            locations.Add(new TPloc { Name = "villanelle", Info = "Villanelle, Lutesong Harbor", X = 21178, Y = 10604, Z = 119, altNames = new string[] { "villa" } });
            locations.Add(new TPloc { Name = "windscour", Info = "Windscour Savanah, Skyfang", X = 25926, Y = 6855, Z = 362, altNames = new string[] { "windscoure","ws"} });
            locations.Add(new TPloc { Name = "ynyster", Info = "Ynyster, Caernord", X = 20931, Y = 13158, Z = 116, altNames = new string[] { "ynys" } });
            
            // Auroria
            locations.Add(new TPloc { Name = "calmlands", Info = "Calmlands", X = 22349, Y = 24941, Z = 189 });
            locations.Add(new TPloc { Name = "diamondshores", Info = "Diamond Shores", X = 19332, Y = 26883, Z = 130, altNames = new string[] { "ds","auroria","origin"} });
            locations.Add(new TPloc { Name = "exeloch", Info = "Exeloch", X = 22349, Y = 24941, Z = 189, altNames = new string[] { "exe" } });
            locations.Add(new TPloc { Name = "goldenruins", Info = "Golden Ruins", X = 17230, Y = 27501, Z = 141, altNames = new string[] { "golden" } });
            locations.Add(new TPloc { Name = "heedmar", Info = "Heedmar", X = 20110, Y = 24568, Z = 156 });
            locations.Add(new TPloc { Name = "marcala", Info = "Marcala", X = 20140, Y = 24768, Z = 189 });
            locations.Add(new TPloc { Name = "nuimari", Info = "Nuimari", X = 21731, Y = 23751, Z = 146 });
            locations.Add(new TPloc { Name = "reedwind", Info = "Reedwind", X = 19628, Y = 28339, Z = 295 });
            locations.Add(new TPloc { Name = "sungold", Info = "Sungold Fields", X = 22349, Y = 24941, Z = 189 });
            // locations.Add(new TPloc { Name = "whalesong", Info = "Whalesong Harbor", X = 14436, Y = 26696, Z = 135, altNames = ("whale"} });

            // Others
            // locations.Add(new TPloc { Name = "aegisisland", Info = "Aegis Island", X = 14436, Y = 26696, Z = 134, altNames = ("aegis"} });
            locations.Add(new TPloc { Name = "freedich", Info = "Sunspeck Sea, Freedich Island", X = 20944, Y = 18799, Z = 134, altNames = new string[] { "freeditch","free"} });
            locations.Add(new TPloc { Name = "growlgate", Info = "Stormraw Sound, Growlgate Island", X = 15138, Y = 22983, Z = 105, altNames = new string[] { "pirate" } });
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 1){
                var n = args[0].ToLower();
                bool foundIt = false;
                foreach (TPloc item in locations)
                {
                    if (item.Name == n)
                    {
                        foundIt = true;
                    }
                    else if (item.altNames != null)
                    {
                        foreach (string alt in item.altNames)
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
                        character.SendMessage("Teleporting to |cFFFFFFFF" + item.Info + "|r");
                        character.DisabledSetPosition = true;
                        character.SendPacket(new SCTeleportUnitPacket(0, 0, item.X, item.Y, item.Z, 0));

                        break;
                    }
                }
                if (!foundIt)
                {
                    character.SendMessage("|cFFFF0000Unavailable Location [" + args[0] + "]|r");
                }
                /*
                TPloc result = locations.Find(o => (o.Name == args[0].ToLower()));
                if(result != null){
                    character.SendMessage("Teleporting to : " + result.Info);
                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(0, 0, result.X, result.Y, result.Z, 0));
                }
                else
                {
                    character.SendMessage("Unavailable Location ["+ args[0] +"]");
                }
                */
            }
            else
            {
                character.SendMessage("Usage : /teleport <Location>");
                character.SendMessage("Available locations :");
                foreach(TPloc item in locations){
                    character.SendMessage(item.Info + " (|cFFFFFFFF" + item.Name + "|r)");
                }
            }
        }
    }

    public class TPloc{
        public string Name {get; set;}
        public string Info {get; set;}
        public int X {get; set;}
        public int Y {get; set;}
        public int Z {get; set;}
        public string[] altNames { get; set; }
    }
}

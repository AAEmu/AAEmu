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

        public void loadLocations(){
            locations.Add( new TPloc{ Name = "hellswamp", Info = "Hellswamp, Anarchi", X = 7482, Y = 9853, Z = 189 });
            locations.Add( new TPloc{ Name = "tigerspine", Info = "Tigerspine Mountains, Anvilton", X = 21772, Y = 8089, Z = 404 });
            locations.Add( new TPloc{ Name = "solis", Info = "Solis Headlands, Austera", X = 16743, Y = 9018, Z = 120 });
            locations.Add( new TPloc{ Name = "whitearden", Info = "White Arden, Birchkeep", X = 9682, Y = 12775, Z = 161 });
            locations.Add( new TPloc{ Name = "ynyster", Info = "Ynyster, Caernord", X = 20931, Y = 13158, Z = 116 });
            locations.Add( new TPloc{ Name = "solzreed", Info = "Solzreed Peninsula, Cresent Throne", X = 15369, Y = 13864, Z = 159 });
            locations.Add( new TPloc{ Name = "twocrowns", Info = "Two Crowns, Ezna", X = 13500, Y = 10531, Z = 223 });
            locations.Add( new TPloc{ Name = "falcorth", Info = "Falcorth Plains, Oxion Clan", X = 23818, Y = 9102, Z = 589 });
            locations.Add( new TPloc{ Name = "freedich", Info = "Sunspeck Sea, Freedich Island", X = 20944, Y = 18799, Z = 134 });
            locations.Add( new TPloc{ Name = "sanddeep", Info = "Sanddeep, Golden Fable Harbor", X = 10720, Y = 9591, Z = 105 });
            locations.Add( new TPloc{ Name = "growlgate", Info = "Stormraw Sound, Growlgate Island", X = 15138, Y = 22983, Z = 105 });
            locations.Add( new TPloc{ Name = "karkasse", Info = "Karkasse Ridgelands, Lavis", X = 10470, Y = 17346, Z = 186 });
            locations.Add( new TPloc{ Name = "villanelle", Info = "Villanelle, Lutesong Harbor", X = 21178, Y = 10604, Z = 119 });
            locations.Add( new TPloc{ Name = "mahadevi", Info = "Mahadevi, City of Towers", X = 19284, Y = 8620, Z = 227 });
            locations.Add( new TPloc{ Name = "marianople", Info = "Marianople, Marianople", X = 11144, Y = 12066, Z = 145 });
            locations.Add( new TPloc{ Name = "gweonid", Info = "Gweonid Forest, Memoria", X = 10604, Y = 14925, Z = 280 });
            locations.Add( new TPloc{ Name = "arcumiris", Info = "Arcum Iris, Parchsun Settlement", X = 20509, Y = 7173, Z = 193 });
            locations.Add( new TPloc{ Name = "dewstone", Info = "Dewstone Plains, Sandcloud", X = 12204, Y = 13305, Z = 178 });
            locations.Add( new TPloc{ Name = "cinderstone", Info = "Cinderstone Moor, Seachild Wharf", X = 15400, Y = 11543, Z = 107 });
            locations.Add( new TPloc{ Name = "silentforest", Info = "Silent Forest, Count Sebastians Retreat", X = 23306, Y = 12526, Z = 271 });
            locations.Add( new TPloc{ Name = "windscour", Info = "Windscour Savanah, Skyfang", X = 25926, Y = 6855, Z = 362 });
            locations.Add( new TPloc{ Name = "perinor", Info = "Perinor Ruins, Stonehew", X = 27787, Y = 6732, Z = 572 });
            locations.Add( new TPloc{ Name = "halcyona", Info = "Halcyona, Suns End", X = 9342, Y = 10321, Z = 187 });
            locations.Add( new TPloc{ Name = "hasla", Info = "Hasla, Veroe", X = 30029, Y = 8760, Z = 539 });
            locations.Add( new TPloc{ Name = "rookborne", Info = "Rookborne Basin, Watermist", X = 25388, Y = 9753, Z = 714 });
            locations.Add( new TPloc{ Name = "lilyut", Info = "Lilyut Hills, Windshade", X = 13502, Y = 15035, Z = 210 });
        }
        
        public void Execute(Character character, string[] args)
        {
            if (args.Length == 1){
                TPloc result = locations.Find(o => o.Name == args[0]);
                if(result != null){
                    character.SendMessage("Teleporting to : " + result.Info);
                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(0, 0, result.X, result.Y, result.Z, 0));
                }
                else
                {
                    character.SendMessage("Unavailable Location ["+ args[0] +"]");
                }
            }
            else
            {
                character.SendMessage("Usage : /teleport <Location>");
                character.SendMessage("Available locations :");
                foreach(TPloc item in locations){
                    character.SendMessage(item.Info + " (" + item.Name + ")");
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
    }
}
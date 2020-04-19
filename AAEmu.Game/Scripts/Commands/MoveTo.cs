using NLog;
using System;

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
using AAEmu.Game.Utils;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Scripts.Commands
{
    public class MoveTo : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            CommandManager.Instance.Register("moveto", this);
        }

        public string GetCommandLineHelp()
        {
            return "<rec||save||go||run||walk||back||stop>";
        }

        public string GetCommandHelpText()
        {
            return
                "what is he doing:\n" +
                "- automatically writes the route to the file;\n" +
                "- you can load path data from a file;\n- moves along the route.\n\n" +
                "To start, you need to create the route (s), recording occurs as follows:\n" +
                "1. Start recording;\n" +
                "2. Take a route;\n" +
                "3. Stop recording.\n" +
                "=== here is an example file structure (x, y, z) ===\n" +
                "|15629,0|14989,02|141,2055|\n" +
                "|15628,0|14987,24|141,3826|\n" +
                "|15626,0|14983,88|141,3446|\n" +
                "===================================================;\n";
        }
        public void Execute(Character character, string[] args)
        {
            if (args.Length < 1)
            {
                character.SendMessage("[MoveTo] /moveto <rec||save||go||run||walk||back||stop>");
                return;
            }
            var cmd = args[0];
            character.SendMessage("[MoveTo] cmd: {0}", cmd);
            var moveTo = character.Simulation; // взять AI движения 
            moveTo.npc = (Npc)character.CurrentTarget;
            switch (@cmd)
            {
                case "rec":
                    character.SendMessage("[MoveTo] start recording...");
                    moveTo.StartRecord(moveTo, character);
                    break;
                case "save":
                    character.SendMessage("[MoveTo] finished recording...");
                    moveTo.StopRecord(moveTo);
                    break;
                case "go":
                    character.SendMessage("[MoveTo] walk forward...");
                    moveTo.ReadPath();
                    moveTo.GoToPath((Npc)character.CurrentTarget, true);
                    break;
                case "run":
                    character.SendMessage("[MoveTo] turned on running mode...");
                    moveTo.runningMode = true;
                    break;
                //case "walk": // TODO If you uncomment, it leads to an error, I don’t understand why
                //    character.SendMessage("[MoveTo] turned off running mode...");
                //    moveTo.runningMode = false;
                //    break;
                case "back":
                    character.SendMessage("[MoveTo] walk back...");
                    moveTo.ReadPath();
                    moveTo.GoToPath((Npc)character.CurrentTarget, false);
                    break;
                case "stop":
                    character.SendMessage("[MoveTo] we stand still...");
                    moveTo.StopMove((Npc)character.CurrentTarget);
                    break;
            }
        }
    }
}

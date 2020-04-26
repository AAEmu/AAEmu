using NLog;
using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Units.Route;

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
            return "<rec||save filename||go filename||back filename||stop||run||walk>";
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
            string nameFile = "movefile";
            string cmd = "";
            Simulation moveTo;
            bool run = true;
            // bool walk = false;
            if (args.Length < 1)
            {
                character.SendMessage("[MoveTo] /moveto <rec||save filename||go filename||back filename||stop||run||walk>");
                return;
            }
            if (args[0] == "rec" || args[0] == "stop" || args[0] == "run" || args[0] == "walk")
            {
                cmd = args[0];
            }
            else if (args.Length == 2)
            {
                cmd = args[0];
                nameFile = args[1];
            }
            else
            {
                character.SendMessage("[MoveTo] there should be two parameters, a command and a file_name...");
                return;
            }

            character.SendMessage("[MoveTo] cmd: {0}, nameFile: {1}", cmd, nameFile);
            moveTo = character.Simulation; // take the AI ​​movement
            moveTo.npc = (Npc)character.CurrentTarget;
            if (moveTo.npc == null)
            {
                character.SendMessage("[MoveTo] You need a target NPC to manage it!");
            }
            else
            {
                switch (cmd)
                {
                    case "rec":
                        character.SendMessage("[MoveTo] start recording...");
                        moveTo.StartRecord(moveTo, character);
                        break;
                    case "save":
                        character.SendMessage("[MoveTo] have finished recording...");
                        moveTo.MoveFileName = nameFile;
                        moveTo.StopRecord(moveTo);
                        break;
                    case "go":
                        character.SendMessage("[MoveTo] walk go...");
                        moveTo.MoveFileName = nameFile;
                        moveTo.ReadPath();
                        moveTo.GoToPath((Npc)character.CurrentTarget, true);
                        break;
                    case "back":
                        character.SendMessage("[MoveTo] walk back...");
                        moveTo.MoveFileName = nameFile;
                        moveTo.ReadPath();
                        moveTo.GoToPath((Npc)character.CurrentTarget, false);
                        break;
                    case "run":
                        character.SendMessage("[MoveTo] turned on running mode...");
                        moveTo.runningMode = run;
                        break;
                    //case "walk":
                    //    character.SendMessage("[MoveTo] turned off running mode...");
                    //    moveTo.runningMode = walk;
                    //    break;
                    case "stop":
                        character.SendMessage("[MoveTo] standing still...");
                        moveTo.StopMove((Npc)character.CurrentTarget);
                        break;
                }
            }
        }
    }
}

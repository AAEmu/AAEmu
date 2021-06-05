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
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands
{
    public class Dloc : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            CommandManager.Instance.Register("dloc", this);
        }

        public string GetCommandLineHelp()
        {
            return "(doodadID) <x> <y> <z>";
        }

        public string GetCommandHelpText()
        {
            return "change doodad position";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 4)
            {
                character.SendMessage("[dloc] /dloc <doodadID> <x> <y> <z> - Use x y z instead of a value to keep current position");
                return;
            }

            if (uint.TryParse(args[0], out var id))
            {
                var doodad = WorldManager.Instance.GetDoodad(id);
                if (doodad != null)
                {
                    float value = 0;
                    float x = doodad.Position.X;
                    float y = doodad.Position.Y;
                    float z = doodad.Position.Z;

                    if (float.TryParse(args[1], out value) && args[1] != "x")
                    {
                        x = value;
                    }

                    if (float.TryParse(args[2], out value) && args[2] != "y")
                    {
                        y = value;
                    }

                    if (float.TryParse(args[3], out value) && args[3] != "z")
                    {
                        z = value;
                    }

                    doodad.Position.X = x;
                    doodad.Position.Y = y;
                    doodad.Position.Z = z;
                    
                    doodad.Hide();
                    doodad.Show();
                }
                else
                {
                    character.SendMessage("[dloc] doodad is null!");
                }
            }
        }
    }
}

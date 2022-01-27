using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Commons.Utils;
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands
{
    public class Npcloc : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            CommandManager.Instance.Register("npcloc", this);
        }

        public string GetCommandLineHelp()
        {
            return "(npcObjID) <x> <y> <z>";
        }

        public string GetCommandHelpText()
        {
            return "change doodad position";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 4)
            {
                character.SendMessage( "[npcloc] /npcloc <npcObjID> <x> <y> <z> - Use x y z instead of a value to keep current position" );
                return;
            }

            if (uint.TryParse(args[0], out var id))
            {
                var npc = WorldManager.Instance.GetNpc(id);
                if ( npc != null)
                {
                    float value = 0;
                    float x = npc.Transform.Local.Position.X;
                    float y = npc.Transform.Local.Position.Y;
                    float z = npc.Transform.Local.Position.Z;

                    if (args[1] != "x" && float.TryParse(args[1], out value))
                    {
                        x = value;
                    }

                    if (args[2] != "y" && float.TryParse(args[2], out value))
                    {
                        y = value;
                    }

                    if (args[3] != "z" && float.TryParse(args[3], out value))
                    {
                        z = value;
                    }

                    npc.Transform.Local.SetPosition(x,y,z);

                    npc.Hide();
                    npc.Show();
                }
                else
                {
                    character.SendMessage("[dloc] doodad is null!");
                }
            }
        }
    }
}

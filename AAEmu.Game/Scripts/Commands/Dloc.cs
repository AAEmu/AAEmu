using AAEmu.Game.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using NLog;

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

        public void Execute(ICharacter character, string[] args)
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
                    float x = doodad.Transform.Local.Position.X;
                    float y = doodad.Transform.Local.Position.Y;
                    float z = doodad.Transform.Local.Position.Z;

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

                    doodad.Transform.Local.SetPosition(x,y,z);
                    
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

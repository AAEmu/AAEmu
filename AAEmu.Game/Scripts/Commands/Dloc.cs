using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using NLog;

namespace AAEmu.Game.Scripts.Commands;

public class Dloc : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "doodad_location", "doodadlocation", "dloc" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<doodad objId> <x> <y> <z>";
    }

    public string GetCommandHelpText()
    {
        return "Change doodad location. You can use * instead of the x y or z values to keep their original position.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 4)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (uint.TryParse(args[0], out var id))
        {
            var doodad = WorldManager.Instance.GetDoodad(id);
            if (doodad != null)
            {
                var value = 0f;
                var x = doodad.Transform.Local.Position.X;
                var y = doodad.Transform.Local.Position.Y;
                var z = doodad.Transform.Local.Position.Z;

                if (args[1] != "*" && args[1] != "x" && float.TryParse(args[1], out value))
                {
                    x = value;
                }

                if (args[2] != "*" && args[2] != "y" && float.TryParse(args[2], out value))
                {
                    y = value;
                }

                if (args[3] != "*" && args[3] != "z" && float.TryParse(args[3], out value))
                {
                    z = value;
                }

                doodad.Transform.Local.SetPosition(x, y, z);

                doodad.Hide();
                doodad.Show();
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, $"Doodad with objId {id} does not exist");
            }
        }
        else
        {
            CommandManager.SendErrorText(this, messageOutput, $"<doodad objId> parse error");
        }
    }
}

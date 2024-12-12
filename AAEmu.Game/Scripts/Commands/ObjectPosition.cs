using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ObjectPosition : ICommand
{
    public string[] CommandNames { get; set; } = ["setpos", "npcloc", "npcpos", "doodadloc", "doodadpos"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(ObjID) <x> <y> <z> (can use x y z or * instead of a value to keep current position)";
    }

    public string GetCommandHelpText()
    {
        return "Change a object's local position";
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
            var gameObject = WorldManager.Instance.GetGameObject(id);
            if (gameObject != null)
            {
                var oldX = gameObject.Transform.Local.Position.X;
                var oldY = gameObject.Transform.Local.Position.Y;
                var oldZ = gameObject.Transform.Local.Position.Z;
                var x = gameObject.Transform.Local.Position.X;
                var y = gameObject.Transform.Local.Position.Y;
                var z = gameObject.Transform.Local.Position.Z;

                if (args[1] != "x" && args[1] != "*" && float.TryParse(args[1], out var valueX))
                {
                    x = valueX;
                }

                if (args[2] != "y" && args[2] != "*" && float.TryParse(args[2], out var valueY))
                {
                    y = valueY;
                }

                if (args[3] != "z" && args[3] != "*" && float.TryParse(args[3], out var valueZ))
                {
                    z = valueZ;
                }

                gameObject.Transform.Local.SetPosition(x, y, z);

                gameObject.Hide();
                gameObject.Show();
                CommandManager.SendNormalText(this, messageOutput,
                    $"ObjId: Position {oldX}, {oldY}, {oldZ} => {x}, {y}, {z}");
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput, $"ObjId: {id} not found");
            }
        }
        else
        {
            CommandManager.SendErrorText(this, messageOutput, $"ObjId parse error");
        }
    }
}

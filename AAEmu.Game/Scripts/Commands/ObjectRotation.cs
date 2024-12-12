using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ObjectRotation : ICommand
{
    public string[] CommandNames { get; set; } = ["setrot", "npcrot", "doodadrot"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(ObjID) <x> <y> <z> (can use x y z or * instead of a value to keep current angle)";
    }

    public string GetCommandHelpText()
    {
        return "Change a object's local rotation in degrees";
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
                var oldX = gameObject.Transform.Local.Rotation.X.RadToDeg();
                var oldY = gameObject.Transform.Local.Rotation.Y.RadToDeg();
                var oldZ = gameObject.Transform.Local.Rotation.Z.RadToDeg();
                var roll = gameObject.Transform.Local.Rotation.X.RadToDeg();
                var pitch = gameObject.Transform.Local.Rotation.Y.RadToDeg();
                var yaw = gameObject.Transform.Local.Rotation.Z.RadToDeg();

                if (args[1] != "x" && args[1] != "*" && float.TryParse(args[1], out var valueX))
                {
                    roll = valueX;
                }

                if (args[2] != "y" && args[2] != "*" && float.TryParse(args[2], out var valueY))
                {
                    pitch = valueY;
                }

                if (args[3] != "z" && args[3] != "*" && float.TryParse(args[3], out var valueZ))
                {
                    yaw = valueZ;
                }

                gameObject.Transform.Local.SetRotationDegree(roll, pitch, yaw);

                gameObject.Hide();
                gameObject.Show();
                CommandManager.SendNormalText(this, messageOutput,
                    $"ObjId: r{oldX}, p{oldY}, y{oldZ} => r{roll}, p{pitch}, y{yaw}");
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

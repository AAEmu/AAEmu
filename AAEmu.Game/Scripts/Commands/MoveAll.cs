using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class MoveAll : ICommand
{
    public string[] CommandNames { get; set; } = ["moveall", "move_all"];

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        foreach (var otherChar in WorldManager.Instance.GetAllCharacters())
        {
            if (otherChar != character)
            {
                otherChar.DisabledSetPosition = true;
                otherChar.SendPacket(new SCTeleportUnitPacket(TeleportReason.Gm, 0,
                    character.Transform.World.Position.X, character.Transform.World.Position.Y,
                    character.Transform.World.Position.Z + 1.0f, 0f));
            }
        }
    }

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Moves every player to your location";
    }
}

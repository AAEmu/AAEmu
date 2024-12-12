using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Revive : ICommand
{
    public string[] CommandNames { get; set; } = ["revive"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target)";
    }

    public string GetCommandHelpText()
    {
        return "Revives target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = WorldManager.GetTargetOrSelf(character, args.Length > 0 ? args[0] : null, out var _);
        if (targetPlayer != null)
        {
            if (targetPlayer.Hp == 0)
            {
                targetPlayer.Hp = targetPlayer.MaxHp;
                targetPlayer.Mp = targetPlayer.MaxMp;
                targetPlayer.BroadcastPacket(
                    new SCCharacterResurrectedPacket(targetPlayer.ObjId, targetPlayer.Transform.World.Position.X,
                        targetPlayer.Transform.World.Position.Y, targetPlayer.Transform.World.Position.Z,
                        targetPlayer.Transform.World.Rotation.Z), true);
                targetPlayer.BroadcastPacket(
                    new SCUnitPointsPacket(targetPlayer.ObjId, targetPlayer.Hp, targetPlayer.Mp), true);
                targetPlayer.PostUpdateCurrentHp(targetPlayer, 0, targetPlayer.Hp, KillReason.Unknown);
            }
            else
            {
                character.SendMessage("Target is already alive");
            }
        }
        else
        {
            character.SendMessage("Cannot revive this target");
        }
    }
}

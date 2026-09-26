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
        var targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args.Length > 0 ? args[0] : null, out var _);
        if (targetPlayer != null)
        {
            if (targetPlayer.Hp == 0)
            {
                ReviveCharacter(targetPlayer);
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

    internal static void ReviveCharacter(Character targetPlayer)
    {
        var position = targetPlayer.Transform.World.Position;
        var rotation = targetPlayer.Transform.World.Rotation.Z;

        // Keep the dedicated server's player mirror in the same lifecycle state as World. The
        // client resurrection path uses this order while World HP is still zero, so stale
        // Zone hits cannot kill the player again between resurrection and points sync.
        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayUnitResurrectionToZone?.Invoke(
                targetPlayer.ObjId, position.X, position.Y, position.Z, rotation);
            WorldIntegration.RelayCombatClearedToZone?.Invoke(targetPlayer.ObjId);
        }

        targetPlayer.Hp = targetPlayer.MaxHp;
        targetPlayer.Mp = targetPlayer.MaxMp;

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayUnitPointsToZone?.Invoke(targetPlayer.ObjId, targetPlayer.Hp, targetPlayer.Mp);

        targetPlayer.BroadcastPacket(
            new SCCharacterResurrectedPacket(
                targetPlayer.ObjId, position.X, position.Y, position.Z, rotation), true);
        targetPlayer.BroadcastPacket(
            new SCUnitPointsPacket(targetPlayer.ObjId, targetPlayer.Hp, targetPlayer.Mp), true);
        targetPlayer.PostUpdateCurrentHp(targetPlayer, 0, targetPlayer.Hp, KillReason.Unknown);
    }
}

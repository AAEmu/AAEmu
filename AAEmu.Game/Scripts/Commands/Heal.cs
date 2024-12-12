using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Heal : ICommand
{
    public string[] CommandNames { get; set; } = ["heal"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) or self";
    }

    public string GetCommandHelpText()
    {
        return "Heals target or self if no target supplied";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var playerTarget = character.CurrentTarget;

        var chatTarget = args.Length > 0 ? args[0] : "";
        var targetPlayer = WorldManager.Instance.GetCharacter(chatTarget);
        if (chatTarget != string.Empty && targetPlayer != null)
        {
            playerTarget = targetPlayer;
        }

        if (targetPlayer != null && playerTarget != null)
        {
            if (targetPlayer.Hp == 0)
            {
                // This check is needed otherwise the player will be kicked
                CommandManager.SendErrorText(this, messageOutput,
                    "Cannot heal a dead target, use the revive command instead");
            }
            else
            {
                var oldHp = targetPlayer.Hp;
                targetPlayer.Hp = targetPlayer.MaxHp;
                targetPlayer.Mp = targetPlayer.MaxMp;
                targetPlayer.BroadcastPacket(
                    new SCUnitPointsPacket(targetPlayer.ObjId, targetPlayer.Hp, targetPlayer.Mp), true);
                targetPlayer.PostUpdateCurrentHp(targetPlayer, oldHp, targetPlayer.Hp, KillReason.Unknown);
            }
        }
        else if (playerTarget is Unit unit)
        {
            // Player is trying to heal some other unit
            if (unit.Hp == 0)
            {
                CommandManager.SendErrorText(this, messageOutput, "Cannot heal a dead target");
            }
            else
            {
                var oldHp = unit.Hp;
                unit.Hp = unit.MaxHp;
                unit.Mp = unit.MaxMp;
                unit.BroadcastPacket(new SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp), true);
                character.SendMessage($"{unit.Name} => {unit.Hp}/{unit.MaxHp} HP, {unit.Mp}/{unit.MaxMp} MP");
                unit.PostUpdateCurrentHp(unit, oldHp, unit.Hp, KillReason.Unknown);
            }
        }
    }
}

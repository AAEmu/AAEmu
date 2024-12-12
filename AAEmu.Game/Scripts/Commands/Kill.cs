using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Kill : ICommand
{
    public string[] CommandNames { get; set; } = ["kill"];

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
        return "Kills target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var targetPlayer = WorldManager.GetTargetOrSelf(character, null, out var _);
        var playerTarget = character.CurrentTarget;
        if (playerTarget is Unit aUnit)
        {
            // Player is trying to kill an NPC/Monster
            if (aUnit.Hp == 0)
            {
                CommandManager.SendErrorText(this, messageOutput, "Target is already dead");
            }
            else
            {
                // We must broadcast this package because if character had initially attacked the mob and then executed kill
                // the mob's "ghost" will still be attacking you and draining HP even though he doesn't exist in the world anymore
                aUnit.CurrentTarget = null;

                // Don't directly do the DoDie(), but trigger a damage with enough damage to kill target
                // no matter what (even if it somehow still had hp after settings it to 0)
                aUnit.ReduceCurrentHp(character, (aUnit.MaxHp + 1) * 10, KillReason.Gm);
            }
        }
        else
        {
            CommandManager.SendNormalText(this, messageOutput, $"Cannot kill this target");
        }

        character.IsInBattle = false; // In case the character gets stuck in battle mode after engaging a mob
    }
}

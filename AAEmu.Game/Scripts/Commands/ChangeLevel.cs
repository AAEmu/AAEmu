using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ChangeLevel : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "level", "setlevel", "set_level", "change_level", "changelevel" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(target) <level>";
    }

    public string GetCommandHelpText()
    {
        return $"Adds experience points needed to reach target <level>\n" +
               $"Allowed range is 2-{ExperienceManager.MaxPlayerLevel} and {ExperienceManager.MaxMateLevel} for pets\n";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            //character.SendMessage("[Level] level: 1-100");
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstArg);

        byte level = 0;
        if (byte.TryParse(args[firstArg + 0], out var parseLevel))
        {
            level = parseLevel;
        }

        if (character.CurrentTarget is Mate mate)
        {
            if (level <= 1 || level > ExperienceManager.MaxMateLevel)
            {
                CommandManager.SendErrorText(this, messageOutput,
                    $"Allowed level range: 2-{ExperienceManager.MaxMateLevel}|r");
                return;
            }

            var expForTargetLevel = ExperienceManager.Instance.GetExpForLevel(level, true) - mate.Experience;
            if (expForTargetLevel > 0)
            {
                mate.AddExp(expForTargetLevel);
            }

            if (level > mate.Level)
            {
                mate.BroadcastPacket(new SCLevelChangedPacket(mate.ObjId, level), true);
            }
        }
        else
        {
            if (level <= 1 || level > ExperienceManager.MaxPlayerLevel)
            {
                CommandManager.SendErrorText(this, messageOutput,
                    $"Allowed level range: 2-{ExperienceManager.MaxPlayerLevel}|r");
                return;
            }

            var maxExpToAdd = 0;

            if (targetPlayer.Ability1 != AbilityType.None)
            {
                var expForA1 =
                    ExperienceManager.Instance.GetExpNeededToGivenLevel(
                        targetPlayer.Abilities.Abilities[targetPlayer.Ability1].Exp, level);
                if (expForA1 > maxExpToAdd)
                {
                    maxExpToAdd = expForA1;
                }
            }

            if (targetPlayer.Ability2 != AbilityType.None)
            {
                var expForA2 =
                    ExperienceManager.Instance.GetExpNeededToGivenLevel(
                        targetPlayer.Abilities.Abilities[targetPlayer.Ability2].Exp, level);
                if (expForA2 > maxExpToAdd)
                {
                    maxExpToAdd = expForA2;
                }
            }

            if (targetPlayer.Ability3 != AbilityType.None)
            {
                var expForA3 =
                    ExperienceManager.Instance.GetExpNeededToGivenLevel(
                        targetPlayer.Abilities.Abilities[targetPlayer.Ability3].Exp, level);
                if (expForA3 > maxExpToAdd)
                {
                    maxExpToAdd = expForA3;
                }
            }

            var expForLevel = ExperienceManager.Instance.GetExpForLevel(level) - targetPlayer.Experience;
            if (expForLevel > maxExpToAdd)
            {
                maxExpToAdd = expForLevel;
            }

            // Add maximum required xp to get to target levels
            if (maxExpToAdd > 0)
            {
                targetPlayer.AddExp(maxExpToAdd, true);
            }

            // If the target level is bigger than player's current level, refill HP/MP and send level-up packet
            if (level > targetPlayer.Level)
            {
                targetPlayer.Level = level;
                targetPlayer.Experience = expForLevel;

                targetPlayer.Hp = targetPlayer.MaxHp;
                targetPlayer.Mp = targetPlayer.MaxMp;

                targetPlayer.BroadcastPacket(new SCLevelChangedPacket(targetPlayer.ObjId, level), true);
            }
        }
    }
}

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ChangeLevel : ICommand
{
    public void OnLoad()
    {
        string[] name = { "setlevel", "set_level", "change_level", "changelevel", "level" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "<level>";
    }

    public string GetCommandHelpText()
    {
        return "Adds experience points needed to reach target <level> (allowed range is 1-55)\n" +
            "Do note that going above the intended max level might break skills.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage("[Level] " + CommandManager.CommandPrefix + "set_level (target) <level>");
            //character.SendMessage("[Level] level: 1-100");
            return;
        }

        var targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstarg);

        byte level = 0;
        if (byte.TryParse(args[firstarg + 0], out byte parseLevel))
        {
            level = parseLevel;
        }

        if (level <= 0 || level > ExperienceManager.MaxPlayerLevel)
        {
            character.SendMessage($"|cFFFF0000[Level] Allowed level range: 1-{ExperienceManager.MaxPlayerLevel}|r");
            return;
        }

        var maxExpToAdd = 0;

        if (targetPlayer.Ability1 != AbilityType.None)
        {
            var expForA1 = ExperienceManager.Instance.GetExpNeededToGivenLevel(targetPlayer.Abilities.Abilities[targetPlayer.Ability1].Exp, level);
            if (expForA1 > maxExpToAdd)
                maxExpToAdd = expForA1;
        }

        if (targetPlayer.Ability2 != AbilityType.None)
        {
            var expForA2 = ExperienceManager.Instance.GetExpNeededToGivenLevel(targetPlayer.Abilities.Abilities[targetPlayer.Ability2].Exp, level);
            if (expForA2 > maxExpToAdd)
                maxExpToAdd = expForA2;
        }

        if (targetPlayer.Ability3 != AbilityType.None)
        {
            var expForA3 = ExperienceManager.Instance.GetExpNeededToGivenLevel(targetPlayer.Abilities.Abilities[targetPlayer.Ability3].Exp, level);
            if (expForA3 > maxExpToAdd)
                maxExpToAdd = expForA3;
        }

        var expForLevel = ExperienceManager.Instance.GetExpForLevel(level) - targetPlayer.Experience;
        if (expForLevel > maxExpToAdd)
            maxExpToAdd = expForLevel;

        // Add maximum required xp to get to target levels
        if (maxExpToAdd > 0)
            targetPlayer.AddExp(maxExpToAdd, true);

        // If the target level is bigger than player's current level, refill HP/MP and send level-up packet
        if (level > targetPlayer.Level)
        {
            targetPlayer.Level = level;
            targetPlayer.Experience = expForLevel;

            targetPlayer.Hp = targetPlayer.MaxHp;
            targetPlayer.Mp = targetPlayer.MaxMp;

            targetPlayer.SendPacket(new SCLevelChangedPacket(targetPlayer.ObjId, level));
        }
    }
}

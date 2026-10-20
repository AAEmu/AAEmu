using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>Resolves the learned rank for a character skill from its progression source.</summary>
public static class SkillRankRules
{
    public static byte ForUnit(SkillTemplate template, Unit owner)
    {
        if (template == null || owner == null || template.LevelStep <= 0)
            return 1;

        // Racial forms progress with character level; they are not gated by XP in a normal skill tree.
        var progressionLevel = template.CharRaceId > 0 && owner is Character character
            ? character.Level
            : owner.GetAbLevel(template.AbilityId);
        var rank = (progressionLevel - template.AbilityLevel) / template.LevelStep + 1;
        return (byte)Math.Clamp(rank, 1, byte.MaxValue);
    }
}

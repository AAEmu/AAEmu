using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillRankRulesTests
{
    private sealed class StubUnit : Unit
    {
        private readonly AbilityType _abilityType;
        private readonly int _level;

        public StubUnit(AbilityType abilityType, int level)
        {
            _abilityType = abilityType;
            _level = level;
        }

        public override int GetAbLevel(AbilityType type) =>
            type == _abilityType ? _level : base.GetAbLevel(type);
    }

    [Test]
    public async Task MissingStep_IsRankOne()
    {
        var template = new SkillTemplate { LevelStep = 0, AbilityLevel = 1 };
        await Assert.That(SkillRankRules.ForUnit(template, new StubUnit(AbilityType.Fight, 30))).IsEqualTo((byte)1);
    }

    [Test]
    public async Task AbilitySkill_UsesAbilityLevel()
    {
        var template = new SkillTemplate
        {
            LevelStep = 5,
            AbilityLevel = 10,
            AbilityId = AbilityType.Fight,
            CharRaceId = 0
        };
        var owner = new StubUnit(AbilityType.Fight, 30);
        await Assert.That(SkillRankRules.ForUnit(template, owner)).IsEqualTo((byte)((30 - 10) / 5 + 1));
    }

    [Test]
    public async Task RacialSkill_UsesCharacterLevel()
    {
        var character = new Character(new UnitCustomModelParams()) { Level = 40 };
        var template = new SkillTemplate
        {
            LevelStep = 5,
            AbilityLevel = 10,
            AbilityId = AbilityType.Fight,
            CharRaceId = 3
        };
        await Assert.That(SkillRankRules.ForUnit(template, character)).IsEqualTo((byte)7);
    }
}

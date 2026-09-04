using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// Regression for same-triad skillsaver activate: two saved allocations of the same trees
/// must restore skills, not early-return on triad equality alone (AAEmu#1546 review).
/// </summary>
public class CharacterAbilitySetsActivationTests
{
    private const AbilityType Tree1 = AbilityType.Fight;
    private const AbilityType Tree2 = AbilityType.Illusion;
    private const AbilityType Tree3 = AbilityType.Wild;

    [Test]
    public async Task TryActivate_SameTriadDifferentSkills_RestoresSavedAllocation()
    {
        var character = CreateCharacter();
        SeedSkill(character, 101, Tree1);
        SeedSkill(character, 102, Tree1);
        SeedSkill(character, 201, Tree1);
        SeedSkill(character, 202, Tree1);

        var slotA = SameTriadSlot(0, [101u, 102u]);
        var slotB = SameTriadSlot(1, [201u, 202u]);
        character.AbilitySets.SeedSlotForTests(slotA, usableSlotCount: 2);
        character.AbilitySets.SeedSlotForTests(slotB, usableSlotCount: 2);
        character.AbilitySets.BypassActivationChargeForTests = true;

        Learn(character, 101, Tree1);
        Learn(character, 102, Tree1);

        await Assert.That(character.AbilitySets.TryActivate(1)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(201)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(202)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(101)).IsFalse();
        await Assert.That(character.Skills.Skills.ContainsKey(102)).IsFalse();
        await Assert.That(character.Ability1).IsEqualTo(Tree1);
        await Assert.That(character.Ability2).IsEqualTo(Tree2);
        await Assert.That(character.Ability3).IsEqualTo(Tree3);
    }

    [Test]
    public async Task TryActivate_SameTriadMatchingSkills_IsNoOp()
    {
        var character = CreateCharacter();
        SeedSkill(character, 101, Tree1);
        SeedSkill(character, 102, Tree1);

        var slot = SameTriadSlot(0, [101u, 102u]);
        character.AbilitySets.SeedSlotForTests(slot, usableSlotCount: 1);
        character.AbilitySets.BypassActivationChargeForTests = true;

        Learn(character, 101, Tree1);
        Learn(character, 102, Tree1);

        await Assert.That(character.AbilitySets.TryActivate(0)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(101)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(102)).IsTrue();
        await Assert.That(character.Skills.Skills.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TryActivate_SameTriadAfterReallocate_RestoresSnapshot()
    {
        var character = CreateCharacter();
        SeedSkill(character, 101, Tree1);
        SeedSkill(character, 102, Tree1);
        SeedSkill(character, 103, Tree1);

        var saved = SameTriadSlot(0, [101u, 102u]);
        character.AbilitySets.SeedSlotForTests(saved, usableSlotCount: 1);
        character.AbilitySets.BypassActivationChargeForTests = true;

        // Player reallocated while keeping the same three trees.
        Learn(character, 103, Tree1);

        await Assert.That(character.AbilitySets.TryActivate(0)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(101)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(102)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(103)).IsFalse();
    }

    private static Character CreateCharacter()
    {
        var character = new Character(new UnitCustomModelParams())
        {
            Id = 42,
            Name = "SkillsaverTester",
            Level = 50,
            Ability1 = Tree1,
            Ability2 = Tree2,
            Ability3 = Tree3
        };
        character.Abilities = new CharacterAbilities(character);
        character.Abilities.SetAbility(Tree1, 0);
        character.Abilities.SetAbility(Tree2, 1);
        character.Abilities.SetAbility(Tree3, 2);
        character.Skills = new CharacterSkills(character);
        character.AbilitySets = new CharacterAbilitySets(character);
        return character;
    }

    private static AbilitySetSlot SameTriadSlot(byte index, uint[] skillIds)
    {
        var slot = new AbilitySetSlot
        {
            SlotIndex = index,
            Ability1 = Tree1,
            Ability2 = Tree2,
            Ability3 = Tree3
        };
        slot.SkillIds.AddRange(skillIds);
        return slot;
    }

    private static void SeedSkill(Character character, uint id, AbilityType tree)
    {
        character.AbilitySets.SeedSkillTemplateForTests(new SkillTemplate
        {
            Id = id,
            AbilityId = tree,
            SkillPoints = 0
        });
    }

    private static void Learn(Character character, uint skillId, AbilityType tree)
    {
        character.Skills.Skills[skillId] = new Skill
        {
            Id = skillId,
            Template = new SkillTemplate { Id = skillId, AbilityId = tree, SkillPoints = 0 },
            Level = 1
        };
    }
}

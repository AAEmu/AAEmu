using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// Ability swap / skillsaver activate must publish newly seeded combat-resource
/// totals (AAEmu#1554). Helper-only seed tests cannot catch a missing SendAll.
/// </summary>
[NotInParallel]
public class CharacterCombatResourceAbilityChangeTests
{
    private const int FightResourceId = 1;
    private const int JoyResourceId = 26;
    private const int SorrowResourceId = 27;
    private const int HeldFightAmount = 3;
    private const int PleasureDefault = 5;

    [After(Test)]
    public void ClearCombatResourceTables() => CombatResourceGameData.Instance.ClearForTests();

    [Test]
    public async Task TryActivate_IntoPleasure_PublishesSeededTotalsAndKeepsHeldFight()
    {
        SeedFightAndPleasureTables();
        var character = CreateCharacter(AbilityType.Fight, AbilityType.Illusion, AbilityType.Wild);
        character.CombatResources[FightResourceId] = HeldFightAmount;
        character.AbilitySets.SeedSlotForTests(new AbilitySetSlot
        {
            SlotIndex = 0,
            Ability1 = AbilityType.Fight,
            Ability2 = AbilityType.Illusion,
            Ability3 = AbilityType.Pleasure
        });
        character.AbilitySets.BypassActivationChargeForTests = true;
        character.StartCombatResourcePointLog();

        await Assert.That(character.AbilitySets.TryActivate(0)).IsTrue();

        await Assert.That(character.Ability3).IsEqualTo(AbilityType.Pleasure);
        await Assert.That(character.GetCombatResource(FightResourceId)).IsEqualTo(HeldFightAmount);
        await Assert.That(character.GetCombatResource(JoyResourceId)).IsEqualTo(PleasureDefault);
        await Assert.That(character.GetCombatResource(SorrowResourceId)).IsEqualTo(PleasureDefault);
        var published = LastPublished(character);
        await Assert.That(published[JoyResourceId]).IsEqualTo(PleasureDefault);
        await Assert.That(published[SorrowResourceId]).IsEqualTo(PleasureDefault);
        await Assert.That(published[FightResourceId]).IsEqualTo(HeldFightAmount);
    }

    [Test]
    public async Task TryActivate_SameTriad_DoesNotPublishCombatResources()
    {
        SeedFightAndPleasureTables();
        var character = CreateCharacter(AbilityType.Fight, AbilityType.Illusion, AbilityType.Pleasure);
        character.CombatResources[JoyResourceId] = PleasureDefault;
        character.CombatResources[SorrowResourceId] = PleasureDefault;
        character.AbilitySets.SeedSlotForTests(new AbilitySetSlot
        {
            SlotIndex = 0,
            Ability1 = AbilityType.Fight,
            Ability2 = AbilityType.Illusion,
            Ability3 = AbilityType.Pleasure
        });
        character.AbilitySets.BypassActivationChargeForTests = true;
        character.StartCombatResourcePointLog();

        await Assert.That(character.AbilitySets.TryActivate(0)).IsTrue();
        await Assert.That(character.CombatResourcePointLog).IsEmpty();
        await Assert.That(character.GetCombatResource(JoyResourceId)).IsEqualTo(PleasureDefault);
    }

    [Test]
    public async Task Swap_IntoPleasure_PublishesSeededTotalsAndKeepsHeldFight()
    {
        SeedFightAndPleasureTables();
        var character = CreateCharacter(AbilityType.Fight, AbilityType.Illusion, AbilityType.Wild);
        character.CombatResources[FightResourceId] = HeldFightAmount;
        character.StartCombatResourcePointLog();

        character.Abilities.Swap(AbilityType.Wild, AbilityType.Pleasure);

        await Assert.That(character.Ability3).IsEqualTo(AbilityType.Pleasure);
        await Assert.That(character.GetCombatResource(FightResourceId)).IsEqualTo(HeldFightAmount);
        await Assert.That(character.GetCombatResource(JoyResourceId)).IsEqualTo(PleasureDefault);
        await Assert.That(character.GetCombatResource(SorrowResourceId)).IsEqualTo(PleasureDefault);
        var published = LastPublished(character);
        await Assert.That(published[JoyResourceId]).IsEqualTo(PleasureDefault);
        await Assert.That(published[SorrowResourceId]).IsEqualTo(PleasureDefault);
        await Assert.That(published[FightResourceId]).IsEqualTo(HeldFightAmount);
    }

    [Test]
    public async Task Swap_AwayFromPleasure_PublishesZerosForDroppedPools()
    {
        SeedFightAndPleasureTables();
        var character = CreateCharacter(AbilityType.Fight, AbilityType.Illusion, AbilityType.Pleasure);
        character.CombatResources[FightResourceId] = HeldFightAmount;
        character.CombatResources[JoyResourceId] = PleasureDefault;
        character.CombatResources[SorrowResourceId] = PleasureDefault;
        character.StartCombatResourcePointLog();

        character.Abilities.Swap(AbilityType.Pleasure, AbilityType.Wild);

        await Assert.That(character.Ability3).IsEqualTo(AbilityType.Wild);
        await Assert.That(character.GetCombatResource(JoyResourceId)).IsEqualTo(0);
        await Assert.That(character.GetCombatResource(SorrowResourceId)).IsEqualTo(0);
        await Assert.That(character.GetCombatResource(FightResourceId)).IsEqualTo(HeldFightAmount);
        var published = LastPublished(character);
        await Assert.That(published[JoyResourceId]).IsEqualTo(0);
        await Assert.That(published[SorrowResourceId]).IsEqualTo(0);
        await Assert.That(published[FightResourceId]).IsEqualTo(HeldFightAmount);
    }

    private static Character CreateCharacter(AbilityType ability1, AbilityType ability2, AbilityType ability3)
    {
        var character = new Character(new UnitCustomModelParams())
        {
            Id = 42,
            Name = "CombatResourceSwap",
            Level = 50,
            Ability1 = ability1,
            Ability2 = ability2,
            Ability3 = ability3
        };
        character.Abilities = new CharacterAbilities(character);
        character.Abilities.SetAbility(ability1, 0);
        character.Abilities.SetAbility(ability2, 1);
        character.Abilities.SetAbility(ability3, 2);
        character.Skills = new CharacterSkills(character);
        character.AbilitySets = new CharacterAbilitySets(character);
        character.Abilities.BypassSwapChargeForTests = true;
        return character;
    }

    private static void SeedFightAndPleasureTables()
    {
        CombatResourceGameData.Instance.SeedForTests(
            [
                new CombatResource { Id = FightResourceId, Name = "fight", Max = 5, DefaultPoint = 0, SendTypeId = 1 },
                new CombatResource { Id = JoyResourceId, Name = "joy", Max = 5, DefaultPoint = PleasureDefault, SendTypeId = 1 },
                new CombatResource { Id = SorrowResourceId, Name = "sorrow", Max = 5, DefaultPoint = PleasureDefault, SendTypeId = 1 }
            ],
            new Dictionary<int, HashSet<int>>
            {
                [(int)AbilityType.Fight] = [FightResourceId],
                [(int)AbilityType.Pleasure] = [JoyResourceId, SorrowResourceId]
            });
    }

    private static Dictionary<int, int> LastPublished(Character character)
    {
        var last = new Dictionary<int, int>();
        foreach (var (id, amount) in character.CombatResourcePointLog)
            last[id] = amount;
        return last;
    }
}

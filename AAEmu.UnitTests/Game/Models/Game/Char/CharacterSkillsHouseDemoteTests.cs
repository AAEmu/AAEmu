using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// House remodel/finish skills must leave the permanent book without wiping the rest of General
/// on the server. Client sync is reset-then-<c>SCSkillLearned</c> for remaining General entries.
/// </summary>
public class CharacterSkillsHouseDemoteTests
{
    private const uint KeepGeneralId = 91001;
    private const uint HouseGeneralId = 91002;
    private const uint FightId = 91003;

    [Test]
    public async Task DemoteHouseGrantedSkills_RemovesOnlyTheHouseLearns()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = KeepGeneralId, AbilityId = AbilityType.General }, 1, false);
        character.Skills.AddSkill(new SkillTemplate { Id = HouseGeneralId, AbilityId = AbilityType.General }, 1, false);
        character.Skills.AddSkill(new SkillTemplate { Id = FightId, AbilityId = AbilityType.Fight }, 1, false);

        var demoted = character.Skills.DemoteHouseGrantedSkills([HouseGeneralId]);

        await Assert.That(demoted).IsEqualTo(1);
        await Assert.That(character.Skills.Skills.ContainsKey(HouseGeneralId)).IsFalse();
        await Assert.That(character.Skills.Skills.ContainsKey(KeepGeneralId)).IsTrue();
        await Assert.That(character.Skills.Skills.ContainsKey(FightId)).IsTrue();
        // One General reset — not a Fight reset — so combat trees stay on the client.
        await Assert.That(character.Broadcasts.Count).IsEqualTo(1);
        await Assert.That(character.Broadcasts[0]).IsTypeOf<SCSkillsResetPacket>();
    }

    [Test]
    public async Task DemoteHouseGrantedSkills_NoOpWhenNoneWerePermanent()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = KeepGeneralId, AbilityId = AbilityType.General }, 1, false);

        var demoted = character.Skills.DemoteHouseGrantedSkills([HouseGeneralId]);

        await Assert.That(demoted).IsEqualTo(0);
        await Assert.That(character.Skills.Skills.ContainsKey(KeepGeneralId)).IsTrue();
        await Assert.That(character.Broadcasts).IsEmpty();
    }

    private static RecordingCharacterMock CreateCharacter()
    {
        var character = new RecordingCharacterMock { ObjId = 1, Level = 1 };
        // Character.Skills is built by Load — a fresh host must make it.
        character.Skills = new CharacterSkills(character);
        return character;
    }

    private sealed class RecordingCharacterMock : CharacterMock
    {
        public List<GamePacket> Broadcasts { get; } = [];

        public RecordingCharacterMock() : base() { }

        public override void BroadcastPacket(GamePacket packet, bool self) => Broadcasts.Add(packet);
    }
}

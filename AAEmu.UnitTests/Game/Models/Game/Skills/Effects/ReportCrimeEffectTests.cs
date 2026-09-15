using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class ReportCrimeEffectTests
{
    [Test]
    public async Task ResolveCriminal_CharacterOwnedEvidence_IsTheOwner()
    {
        // The bloodstain belongs to the character who committed the crime; the caster is the witness.
        await Assert.That(ReportCrimeEffect.ResolveCriminalObjId(5, DoodadOwnerType.Character, 9)).IsEqualTo(9u);
    }

    [Test]
    public async Task ResolveCriminal_CasterIsTheOwner_IsRefused()
    {
        // Poking your own evidence must not credit you, mirroring ReportCrime's own-crime refusal.
        await Assert.That(ReportCrimeEffect.ResolveCriminalObjId(5, DoodadOwnerType.Character, 5)).IsEqualTo(0u);
    }

    [Test]
    public async Task ResolveCriminal_NonCharacterEvidence_IsRefused()
    {
        await Assert.That(ReportCrimeEffect.ResolveCriminalObjId(5, DoodadOwnerType.System, 9)).IsEqualTo(0u);
    }

    [Test]
    public async Task ResolveCriminal_MissingOwner_IsRefused()
    {
        await Assert.That(ReportCrimeEffect.ResolveCriminalObjId(5, DoodadOwnerType.Character, 0)).IsEqualTo(0u);
    }

    [Test]
    public async Task ResolveEvidence_NoDoodadTarget_IsNull()
    {
        await Assert.That(ReportCrimeEffect.ResolveEvidence(null, null)).IsNull();
    }

    [Test]
    public async Task ResolveEvidence_ZeroObjId_IsNull()
    {
        var target = new SkillCastDoodadTarget { ObjId = 0 };
        await Assert.That(ReportCrimeEffect.ResolveEvidence(null, target)).IsNull();
    }

    [Test]
    public async Task Range_EvidenceInsideTheSkillReach_IsReportable()
    {
        var witness = CreateCharacter(100f, 200f, 30f);
        var evidence = CreateEvidence(104f, 200f, 30f);

        await Assert.That(ReportCrimeEffect.IsInReportingRange(witness, evidence, skillMaxRange: 5)).IsTrue();
    }

    [Test]
    public async Task Range_EvidenceBeyondTheSkillReach_IsRefused()
    {
        // A doodad target skips the cast pipeline's range check, so a client that knows a remote
        // evidence id must not be able to move another player's crime points from where it stands.
        var witness = CreateCharacter(100f, 200f, 30f);
        var evidence = CreateEvidence(140f, 200f, 30f);

        await Assert.That(ReportCrimeEffect.IsInReportingRange(witness, evidence, skillMaxRange: 5)).IsFalse();
    }

    [Test]
    public async Task Range_SkillWithoutAReach_FallsBackToTheInteractBand()
    {
        var witness = CreateCharacter(100f, 200f, 30f);

        await Assert.That(ReportCrimeEffect.IsInReportingRange(
            witness, CreateEvidence(102f, 200f, 30f), skillMaxRange: 0)).IsTrue();
        await Assert.That(ReportCrimeEffect.IsInReportingRange(
            witness, CreateEvidence(110f, 200f, 30f), skillMaxRange: 0)).IsFalse();
    }

    [Test]
    public async Task Range_WithoutBothSides_IsRefused()
    {
        var witness = CreateCharacter(100f, 200f, 30f);
        var evidence = CreateEvidence(100f, 200f, 30f);

        await Assert.That(ReportCrimeEffect.IsInReportingRange(null, evidence, 5)).IsFalse();
        await Assert.That(ReportCrimeEffect.IsInReportingRange(witness, null, 5)).IsFalse();
    }

    private static Character CreateCharacter(float x, float y, float z)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = 11, Name = "Witness" };
        character.Transform.Local.SetPosition(x, y, z, 0f, 0f, 0f);
        return character;
    }

    private static Doodad CreateEvidence(float x, float y, float z)
    {
        var doodad = new Doodad { ObjId = 4242 };
        doodad.Transform.Local.SetPosition(x, y, z, 0f, 0f, 0f);
        return doodad;
    }
}

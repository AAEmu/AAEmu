using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;

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
}

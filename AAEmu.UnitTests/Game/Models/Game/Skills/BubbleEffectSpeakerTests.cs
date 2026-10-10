using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Who says a bubble line. A sphere-triggered plot cast by the player finds an NPC within range and has
/// it speak; the cast target of the whole plot is still the player.
/// </summary>
public class BubbleEffectSpeakerTests
{
    [Test]
    public async Task PlotResolvedUnit_SpeaksRatherThanTheCastTarget()
    {
        var lieutenant = new BaseUnit { ObjId = 1210 };
        var castTarget = new SkillCastUnitTarget(870);

        await Assert.That(BubbleEffect.SpeakerObjId(lieutenant, castTarget)).IsEqualTo(1210u);
    }

    [Test]
    public async Task UnitTargetedCast_SpeakerIsTheTarget()
    {
        var npc = new BaseUnit { ObjId = 455 };

        await Assert.That(BubbleEffect.SpeakerObjId(npc, new SkillCastUnitTarget(455))).IsEqualTo(455u);
    }

    [Test]
    public async Task NoResolvedUnit_FallsBackToTheCastTarget()
    {
        await Assert.That(BubbleEffect.SpeakerObjId(null, new SkillCastUnitTarget(870))).IsEqualTo(870u);
    }
}

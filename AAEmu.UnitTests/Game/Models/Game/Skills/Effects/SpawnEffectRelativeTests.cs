using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class SpawnEffectRelativeTests
{
    [Test]
    public async Task PosDirTarget_OriDirPlotFacing_UsesTargetForBoth()
    {
        var caster = new Npc();
        var target = new Npc();
        var pos = SpawnEffect.ResolvePositionUnit(1, caster, target);
        var ori = SpawnEffect.ResolveOrientationUnit(3, caster, target, pos);
        await Assert.That(pos).IsEqualTo(target);
        await Assert.That(ori).IsEqualTo(target);
    }

    [Test]
    public async Task OriDirCaster_UsesCaster()
    {
        var caster = new Npc();
        var target = new Npc();
        var pos = SpawnEffect.ResolvePositionUnit(1, caster, target);
        var ori = SpawnEffect.ResolveOrientationUnit(2, caster, target, pos);
        await Assert.That(ori).IsEqualTo(caster);
    }

    [Test]
    public async Task UnknownDir_ReturnsNull()
    {
        var caster = new Npc();
        var target = new Npc();
        await Assert.That(SpawnEffect.ResolvePositionUnit(99, caster, target)).IsNull();
        await Assert.That(SpawnEffect.ResolveOrientationUnit(99, caster, target, target)).IsNull();
    }
}

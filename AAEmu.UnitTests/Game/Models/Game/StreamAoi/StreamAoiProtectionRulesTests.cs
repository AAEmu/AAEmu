using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.StreamAoi;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.StreamAoi;

/// <summary>
/// Which mirrored units a client is not allowed to lose mid-fight. Culling or evicting the unit the
/// player is attacking hides the enemy, and the next attack goes out with no target to resolve.
/// </summary>
public class StreamAoiProtectionRulesTests
{
    [Test]
    public async Task CurrentTarget_IsKept()
    {
        var character = new Character(new UnitCustomModelParams());
        var npc = new Npc();
        character.CurrentTarget = npc;

        await Assert.That(character.MustKeepMirrorStreamed(npc)).IsTrue();
    }

    [Test]
    public async Task UnitAttackingThePlayer_IsKept()
    {
        var character = new Character(new UnitCustomModelParams());
        var npc = new Npc();
        npc.AggroTable[character.ObjId] = new Aggro(character);

        await Assert.That(character.MustKeepMirrorStreamed(npc)).IsTrue();
    }

    [Test]
    public async Task UnrelatedNpc_IsEvictable()
    {
        var character = new Character(new UnitCustomModelParams());
        var npc = new Npc();

        await Assert.That(character.MustKeepMirrorStreamed(npc)).IsFalse();
        await Assert.That(character.MustKeepMirrorStreamed(null)).IsFalse();
    }

    [Test]
    public async Task Rule_KeepsOnEitherCondition()
    {
        await Assert.That(StreamAoiProtectionRules.MustKeepStreamed(isCurrentTarget: true, hasAggroOnMe: false)).IsTrue();
        await Assert.That(StreamAoiProtectionRules.MustKeepStreamed(isCurrentTarget: false, hasAggroOnMe: true)).IsTrue();
        await Assert.That(StreamAoiProtectionRules.MustKeepStreamed(isCurrentTarget: false, hasAggroOnMe: false)).IsFalse();
    }
}

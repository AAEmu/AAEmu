using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The ZoneAuthority helpers that survive on the live cast path. The damage-estimating fallbacks and the
/// leash-skill shape check they fed had no caller outside their own file and are gone: World runs the
/// ordinary skill pipeline for a zone-driven NPC cast, and the leash heal is <c>ResetMirrorNpcHp</c> on
/// ZWClearCombat.
/// </summary>
public class ZoneAuthorityCombatTests
{
    [Test]
    public async Task ResolveZoneCombatActorBc_Null_IsZero()
    {
        await Assert.That(ZoneAuthorityCombat.ResolveZoneCombatActorBc(null)).IsEqualTo(0u);
    }

    [Test]
    public async Task ResolveZoneCombatActorBc_UnitWithoutObjId_IsZero()
    {
        await Assert.That(ZoneAuthorityCombat.ResolveZoneCombatActorBc(new AAEmu.Game.Models.Game.Units.Unit()))
            .IsEqualTo(0u);
    }
}

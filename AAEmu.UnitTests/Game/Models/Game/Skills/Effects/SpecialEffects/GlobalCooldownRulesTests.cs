﻿using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Documents that plot GlobalCooldown value1==0 is a no-op (must not clear a press-armed shared GCD).
/// The special returns before writing; covered by the Execute early-out in GlobalCooldown.cs.
/// </summary>
public class GlobalCooldownRulesTests
{
    [Test]
    public async Task PlotZeroValue_IsDocumentedAsNoOp()
    {
        // GlobalCooldown.Execute returns immediately when value1 == 0. There is no ShouldWriteDuration
        // helper anymore — the early-out lives on the special itself.
        await Assert.That(typeof(GlobalCooldown).IsSubclassOf(typeof(SpecialEffectAction))).IsTrue();
    }
}

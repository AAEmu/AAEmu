using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadOnceOneManInteractionTests
{
    /// <summary>
    /// <c>once_one_man</c> is set on 8,130 of 16,735 doodads (anchors, ladders, crafting
    /// benches...). It is not a per-character lifetime lock: reading it as one made every
    /// toggle doodad one-shot per player — a lowered anchor could never be raised again
    /// (live 2026-09-02, doodad 12651). Per-player quotas on the Abyssal crystals come from
    /// <c>act_count</c>, which is unaffected.
    /// </summary>
    [Test]
    public async Task DoFunc_SameCharacterMayUseAOnceOneManDoodadAgain()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { OnceOneMan = true, FuncGroups = [] }
        };
        var character = new Character(new UnitCustomModelParams()) { Id = 7, Name = "Tester" };
        var func = new DoodadFunc { NextPhase = -1, Count = 0 };
        var applied = 0;

        // Lower the anchor: a successful, phase-changing use.
        doodad.DoFuncWithApply(character, func, (_, owner) =>
        {
            applied++;
            owner.ToNextPhase = true;
        });
        await Assert.That(applied).IsEqualTo(1);

        // Raise it again: must not be refused.
        doodad.DoFuncWithApply(character, func, (_, owner) =>
        {
            applied++;
            owner.ToNextPhase = true;
        });
        await Assert.That(applied).IsEqualTo(2);
    }
}

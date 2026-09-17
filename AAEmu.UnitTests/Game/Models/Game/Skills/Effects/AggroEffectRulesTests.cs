using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// <see cref="AggroEffectRules"/>: the gate that used to require a <see cref="Character"/> caster, which
/// dropped the value from all 353 <c>buff_triggers</c> rows that carry an <c>AggroEffect</c>.
/// </summary>
public class AggroEffectRulesTests
{
    [Test]
    public async Task AnNpcCaster_MayRaiseAggro()
    {
        // Every one of the 353 trigger rows is cast by an Npc, a mate or a slave.
        await Assert.That(AggroEffectRules.CanCast(new Npc { ObjId = 11 })).IsTrue();
        await Assert.That(AggroEffectRules.CanCast(new Mate { ObjId = 12 })).IsTrue();
        await Assert.That(AggroEffectRules.CanCast(new Slave { ObjId = 13 })).IsTrue();
    }

    [Test]
    public async Task ACharacterCaster_StillMay()
    {
        await Assert.That(AggroEffectRules.CanCast(new Character(new UnitCustomModelParams()) { ObjId = 14 })).IsTrue();
    }

    [Test]
    public async Task AnUnspawnedOrWrongTypeCaster_MayNot()
    {
        await Assert.That(AggroEffectRules.CanCast(new Npc { ObjId = 0 })).IsFalse();
        await Assert.That(AggroEffectRules.CanCast(null)).IsFalse();
    }

    [Test]
    public async Task OnlyAnNpcHoldsTheAggroTable()
    {
        await Assert.That(AggroEffectRules.CanHoldAggro(new Npc { ObjId = 11 })).IsTrue();
        await Assert.That(AggroEffectRules.CanHoldAggro(new Character(new UnitCustomModelParams()) { ObjId = 12 })).IsFalse();
        await Assert.That(AggroEffectRules.CanHoldAggro(new Mate { ObjId = 13 })).IsFalse();
        await Assert.That(AggroEffectRules.CanHoldAggro(new Slave { ObjId = 14 })).IsFalse();
        await Assert.That(AggroEffectRules.CanHoldAggro(null)).IsFalse();
        await Assert.That(AggroEffectRules.CanHoldAggro(new Npc { ObjId = 0 })).IsFalse();
    }

    [Test]
    public async Task ANpcCastOnANpc_PassesBothGates()
    {
        // The shape the 353 trigger rows have: an npc casts on the unit it is fighting.
        var caster = new Npc { ObjId = 11 };
        var target = new Npc { ObjId = 12 };

        await Assert.That(AggroEffectRules.CanCast(caster)).IsTrue();
        await Assert.That(AggroEffectRules.CanHoldAggro(target)).IsTrue();
    }
}

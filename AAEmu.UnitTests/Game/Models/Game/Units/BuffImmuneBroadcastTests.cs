using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// The immune hit result is a message about a cast, and it is sent once. A buff's own tick re-applies its
/// effects with a <see cref="CastBuff"/> action (<c>BuffTemplate.DoTick</c> / <c>DoAreaTick</c>), and a
/// buff trigger proc does the same, so an immune unit inside an aura or under a DoT used to send one
/// <see cref="SCUnitDamagedPacket"/> per tick to everyone nearby for the aura's whole life.
/// </summary>
public class BuffImmuneBroadcastTests
{
    private const uint OwnerObjId = 1u;
    private const uint CasterObjId = 2u;
    private const uint AnyBuffId = 91001;

    [Test]
    public async Task BroadcastBuffImmune_ForACast_SendsOneImmunePacket()
    {
        var owner = new RecordingUnit { ObjId = OwnerObjId };
        var caster = new Unit { ObjId = CasterObjId };

        owner.Buffs.BroadcastBuffImmune(caster, new CastSkill(0, 1), new SkillCasterUnit(caster.ObjId));

        await Assert.That(owner.Packets).HasCount().EqualTo(1);
        var packet = (SCUnitDamagedPacket)owner.Packets[0];
        // The same shape DamageEffect's CheckDamageImmune path sends: the immune hit type on 1 damage.
        await Assert.That(packet.HitType).IsEqualTo(SkillHitType.Immune);
    }

    [Test]
    public async Task BroadcastBuffImmune_ForABuffTickOrTrigger_StaysSilent()
    {
        var owner = new RecordingUnit { ObjId = OwnerObjId };
        var caster = new Unit { ObjId = CasterObjId };
        var buff = new Buff(owner, caster, new SkillCasterUnit(caster.ObjId),
            new BuffTemplate { Id = AnyBuffId }, null, DateTime.UtcNow);

        owner.Buffs.BroadcastBuffImmune(caster, new CastBuff(buff), new SkillCasterUnit(caster.ObjId));

        await Assert.That(owner.Packets).IsEmpty();
    }

    private sealed class RecordingUnit : Unit
    {
        public List<GamePacket> Packets { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class BuffCreatedWireTests
{
    private const uint OwnerObjId = 2033;
    private const uint BuffIndex = 140;

    private static Buff Buff(SkillCaster caster)
    {
        var owner = new BaseUnit { ObjId = OwnerObjId };
        var skill = new Skill { Template = new SkillTemplate() };
        return new Buff(owner, new BaseUnit(), caster, new BuffTemplate(), skill, DateTime.UtcNow)
        {
            Index = BuffIndex
        };
    }

    private static byte[] Body(SkillCaster caster)
    {
        var stream = new PacketStream();
        BuffCreatedWire.Write(stream, Buff(caster), forZone: true);
        return stream.GetBytes();
    }

    [Test]
    public async Task TryGetBuffIndex_FindsTheIndexForEveryCasterType()
    {
        // The relay registry keys Update and Remove on the index recovered here. A wrong offset does not
        // fail loudly — it records an index nothing will ever match, so the zone keeps whatever state the
        // Create gave it and every later Update is silently dropped. Every caster type has to be covered:
        // each subclass writes a different number of bytes ahead of the index.
        SkillCaster[] casters =
        [
            new SkillCasterUnit(OwnerObjId),
            new SkillCasterUnk1(OwnerObjId),
            // Item id 0 keeps the setter from reaching for a live ItemManager.
            new SkillItem(OwnerObjId, 0UL, 5678u),
            new SkillCasterMount(OwnerObjId),
            new SkillDoodad(OwnerObjId),
        ];

        foreach (var caster in casters)
        {
            await Assert.That(BuffCreatedWire.TryGetBuffIndex(Body(caster), out var index)).IsTrue();
            await Assert.That(index).IsEqualTo(BuffIndex);
        }
    }

    [Test]
    public async Task TryGetBuffIndex_RejectsATruncatedBody()
    {
        await Assert.That(BuffCreatedWire.TryGetBuffIndex(null, out _)).IsFalse();
        await Assert.That(BuffCreatedWire.TryGetBuffIndex([], out _)).IsFalse();
        await Assert.That(BuffCreatedWire.TryGetBuffIndex([0, 1, 2, 3], out _)).IsFalse();
    }

    [Test]
    public async Task Write_CarriesTheStackCountRatherThanAConstantOne()
    {
        // The zone recomputes stack-scaled attributes from this field, hull speed from sail wind stacks
        // among them, so a Create claiming a single application pins the simulation at one stack.
        var buff = Buff(new SkillCasterUnit(OwnerObjId));
        var stream = new PacketStream();
        BuffCreatedWire.Write(stream, buff, forZone: true);
        var body = stream.GetBytes();

        // Caster (unit: type byte + bc), cast id, target bc, index, buffId, level, abLevel, skillId.
        const int stackOffset = 4 + 8 + 3 + 4 + 4 + 1 + 2 + 4;
        await Assert.That(BitConverter.ToUInt32(body, stackOffset)).IsEqualTo((uint)Math.Max(1, buff.Stack));
        await Assert.That(BuffCreatedWire.TryGetStack(body, out var stack)).IsTrue();
        await Assert.That(stack).IsEqualTo((uint)Math.Max(1, buff.Stack));
    }

    [Test]
    public async Task Write_CarriesGrowingStackCounts()
    {
        foreach (var count in new uint[] { 1, 2, 60 })
        {
            var buff = Buff(new SkillCasterUnit(OwnerObjId));
            buff.Stack = (int)count;
            var stream = new PacketStream();
            BuffCreatedWire.Write(stream, buff, forZone: true);
            await Assert.That(BuffCreatedWire.TryGetStack(stream.GetBytes(), out var stack)).IsTrue();
            await Assert.That(stack).IsEqualTo(count);
        }
    }

    [Test]
    public async Task TryGetStack_RejectsATruncatedBody()
    {
        await Assert.That(BuffCreatedWire.TryGetStack(null, out _)).IsFalse();
        await Assert.That(BuffCreatedWire.TryGetStack([], out _)).IsFalse();
        await Assert.That(BuffCreatedWire.TryGetStack(new byte[10], out _)).IsFalse();
    }
}

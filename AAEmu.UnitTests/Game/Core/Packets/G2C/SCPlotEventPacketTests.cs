using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCPlotEventPacketTests
{
    [Test]
    public async Task ProductionTargetListFiltersSyntheticAndDuplicateIds()
    {
        BaseUnit[] targets =
        [
            new Unit { ObjId = 0x010203 },
            new BaseUnit { ObjId = uint.MaxValue },
            new Unit { ObjId = 0x010203 },
            new Unit { ObjId = 0 },
            new Unit { ObjId = 0x040506 }
        ];

        var ids = PlotTargetInfo.RealTargetUnitIds(targets);
        await Assert.That(ids.Length).IsEqualTo(2);
        await Assert.That(ids[0]).IsEqualTo(0x010203u);
        await Assert.That(ids[1]).IsEqualTo(0x040506u);
    }

    [Test]
    public async Task Write_UsesTheSelectedUnitIdsInOrder()
    {
        uint[] selected = [0x010203, 0x040506, 0x070809];
        var anchor = new BaseUnit { ObjId = uint.MaxValue };
        anchor.Transform = new Transform(anchor, null, 1f, 2f, 3f);
        var body = new SCPlotEventPacket(
                0x1122, 0x33445566, 0x778899AA,
                new PlotObject(0x101112), new PlotObject(anchor.Transform),
                0x131415, 0x1617, 2, 0x18191A1B1C1D1E1F,
                selected, inputDirection: 0x20, channelingTime: 0x2122)
            .Write(new PacketStream());

        var stream = new PacketStream(body.GetBytes());
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0x1122);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0x33445566u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0x778899AAu);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)PlotObjectType.UNIT);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x101112u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)PlotObjectType.POSITION);
        SkipPositionPlotObject(stream);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0x18191A1B1C1D1E1Ful);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x131415u);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0x1617);
        await Assert.That(stream.ReadBc()).IsEqualTo(0u);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0x2122);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);
        await Assert.That(stream.ReadBc()).IsEqualTo(selected[0]);
        await Assert.That(stream.ReadBc()).IsEqualTo(selected[1]);
        await Assert.That(stream.ReadBc()).IsEqualTo(selected[2]);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x20);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Write_EmptySelectionDoesNotRepeatThePlotTarget()
    {
        var body = new SCPlotEventPacket(
                1, 2, 3, new PlotObject(4), new PlotObject(5), 0, 0, 2, targetUnitIds: [])
            .Write(new PacketStream());
        var stream = new PacketStream(body.GetBytes());

        stream.ReadUInt16();
        stream.ReadUInt32();
        stream.ReadUInt32();
        stream.ReadByte();
        stream.ReadBc();
        stream.ReadByte();
        stream.ReadBc();
        stream.ReadUInt64();
        stream.ReadBc();
        stream.ReadUInt16();
        stream.ReadBc();
        stream.ReadUInt16();

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ReplayedClientEventRetainsTheSelectedUnitIds()
    {
        var replay = new PlotClientEvent
        {
            Tl = 1,
            EventId = 2,
            SkillId = 3,
            Caster = new PlotObject(4),
            Target = new PlotObject(0),
            UnkId = 5,
            CastWire = 6,
            Flag = 2,
            TargetUnitIds = [0x010203, 0x040506],
            ChannelWire = 7
        };
        var stream = new PacketStream(replay.ToPacket().Write(new PacketStream()).GetBytes());

        stream.ReadUInt16();
        stream.ReadUInt32();
        stream.ReadUInt32();
        stream.ReadByte();
        stream.ReadBc();
        stream.ReadByte();
        stream.ReadBc();
        stream.ReadUInt64();
        stream.ReadBc();
        stream.ReadUInt16();
        stream.ReadBc();
        stream.ReadUInt16();

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x010203u);
        await Assert.That(stream.ReadBc()).IsEqualTo(0x040506u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    private static void SkipPositionPlotObject(PacketStream stream)
    {
        for (var pose = 0; pose < 2; pose++)
        {
            stream.ReadPosition();
            stream.ReadSByte();
            stream.ReadSByte();
            stream.ReadSByte();
        }
        stream.ReadBc();
        stream.ReadBc();
        stream.ReadBc();
    }
}

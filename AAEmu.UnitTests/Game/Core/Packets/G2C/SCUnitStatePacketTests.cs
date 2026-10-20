using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCUnitStatePacketTests
{
    [Test]
    public async Task Write_NpcWithoutWorld_PreservesPositionAcrossRepeatedSends()
    {
        var npc = new Npc
        {
            ObjId = 44331,
            TemplateId = 5226,
            Template = new NpcTemplate(),
            Name = "Guild Manager"
        };
        var position = new Vector3(19290.992f, 8600.82f, 225f);
        npc.Transform.Local.Position = position;

        for (var i = 0; i < 2; i++)
        {
            var stream = new SCUnitStatePacket(npc).Write(new PacketStream());
            stream.Rollback();

            await Assert.That(npc.Transform.Local.Position).IsEqualTo(position);
            await Assert.That(stream.ReadBc()).IsEqualTo(npc.ObjId);
            await Assert.That(stream.ReadString()).IsEqualTo(npc.Name);
            await Assert.That(stream.ReadByte()).IsEqualTo((byte)BaseUnitType.Npc);
            await Assert.That(stream.ReadBc()).IsEqualTo(npc.ObjId);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(npc.TemplateId);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
            await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
            await Assert.That(stream.ReadString()).IsEqualTo("");

            var sentPosition = stream.ReadPosition();
            await Assert.That(MathF.Abs(sentPosition.x - position.X) < 0.01f).IsTrue();
            await Assert.That(MathF.Abs(sentPosition.y - position.Y) < 0.01f).IsTrue();
            await Assert.That(MathF.Abs(sentPosition.z - position.Z) < 0.01f).IsTrue();
        }
    }
}

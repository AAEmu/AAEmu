using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.World.Core.Packets.Wz;

namespace AAEmu.UnitTests.WorldServer;

/// <summary>
/// The wire half of the gimmick movement relay: the transform record a zone reads back when a world
/// hands it an object it drives.
/// </summary>
[NotInParallel]
public class ZoneGimmickMovementWireTests
{
    private const uint GimmickObjId = 0x0012_3456;
    private const uint GimmickTime = 0x0BAD_F00D;
    private const float GimmickScale = 2.5f;

    [Test]
    public async Task GimmickMovement_EncodesTheSharedTransformRecord()
    {
        // The zone, world and client movement packets all read the same record: an object id and a
        // timestamp, then position, orientation, scale and the two velocity vectors. A zone that
        // cannot read this back cannot simulate the object it was handed.
        var rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        var velocity = new Vector3(1.5f, 0f, -4.5f);
        var angularVelocity = new Vector3(0f, 0.25f, 0f);
        var encoded = new WZGimmickMovementPacket(
            unchecked((int)GimmickObjId), unchecked((int)GimmickTime),
            0x0000_0001_2345_6789UL, 0x0000_0009_8765_4321UL, 42.5f,
            rotation.X, rotation.Y, rotation.Z, rotation.W,
            velocity.X, velocity.Y, velocity.Z,
            angularVelocity.X, angularVelocity.Y, angularVelocity.Z,
            GimmickScale).Encode();

        var frame = new PacketStream(encoded);

        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)(encoded.Length - 2)); // frame length
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.GimmickMovement);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(GimmickObjId);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(GimmickTime);
        await Assert.That(frame.ReadUInt64()).IsEqualTo(0x0000_0001_2345_6789UL);
        await Assert.That(frame.ReadUInt64()).IsEqualTo(0x0000_0009_8765_4321UL);
        await Assert.That(frame.ReadSingle()).IsEqualTo(42.5f);
        await Assert.That(frame.ReadSingle()).IsEqualTo(rotation.X);
        await Assert.That(frame.ReadSingle()).IsEqualTo(rotation.Y);
        await Assert.That(frame.ReadSingle()).IsEqualTo(rotation.Z);
        await Assert.That(frame.ReadSingle()).IsEqualTo(rotation.W);
        await Assert.That(frame.ReadSingle()).IsEqualTo(velocity.X);
        await Assert.That(frame.ReadSingle()).IsEqualTo(velocity.Y);
        await Assert.That(frame.ReadSingle()).IsEqualTo(velocity.Z);
        await Assert.That(frame.ReadSingle()).IsEqualTo(angularVelocity.X);
        await Assert.That(frame.ReadSingle()).IsEqualTo(angularVelocity.Y);
        await Assert.That(frame.ReadSingle()).IsEqualTo(angularVelocity.Z);
        await Assert.That(frame.ReadSingle()).IsEqualTo(GimmickScale);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    [Test]
    public async Task GimmickMovement_OpcodeIsTheProtocolConstant()
    {
        // Guards against a publisher that reaches for the neighbouring create or grasp opcode.
        await Assert.That(WzOpcodes.GimmickMovement).IsEqualTo((ushort)0x005C);
    }
}

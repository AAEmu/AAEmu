using System.Numerics;

using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

public class PlotPoseRotationWireTests
{
    [Test]
    public async Task Encode_Identity_IsZeroBytes()
    {
        var (x, y, z) = PlotPoseRotationWire.Encode(Quaternion.Identity);
        await Assert.That((int)x).IsEqualTo(0);
        await Assert.That((int)y).IsEqualTo(0);
        await Assert.That((int)z).IsEqualTo(0);
    }

    [Test]
    public async Task Encode_PureYawSoutheast_IsNotEulerFacingByte80()
    {
        // SE = yaw −135°. The old PlotObject path wrote ConvertRadianToDirectionRounded → +80.
        // Client axis-angle × 127 expects ~−48 on Z (and 0 on X/Y).
        var q = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, -135f * MathF.PI / 180f));
        var (x, y, z) = PlotPoseRotationWire.Encode(q);
        await Assert.That((int)x).IsEqualTo(0);
        await Assert.That((int)y).IsEqualTo(0);
        await Assert.That((int)z).IsEqualTo(-48);
        await Assert.That((int)z).IsNotEqualTo(80);
    }

    [Test]
    public async Task Encode_Yaw236_MatchesNormalizedSoutheastFamily()
    {
        // Live miss: displayed yaw 236.3° (same heading as −123.7°). Must not emit +84.
        var q = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, 236.3f * MathF.PI / 180f));
        var (x, y, z) = PlotPoseRotationWire.Encode(q);
        await Assert.That((int)x).IsEqualTo(0);
        await Assert.That((int)y).IsEqualTo(0);
        await Assert.That((int)z).IsLessThan(0);
        await Assert.That(Math.Abs((int)z)).IsEqualTo(44).Within(2);
    }

    [Test]
    [Arguments(0f)]
    [Arguments(-90f)]
    [Arguments(90f)]
    [Arguments(180f)]
    [Arguments(-135f)]
    [Arguments(45f)]
    [Arguments(236.3f)]
    public async Task EncodeDecode_PureYaw_RoundTripsFacing(float yawDegrees)
    {
        var yaw = yawDegrees * MathF.PI / 180f;
        var q = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, yaw));
        var (x, y, z) = PlotPoseRotationWire.Encode(q);
        var back = PlotPoseRotationWire.Decode(x, y, z);
        var euler = PositionAndRotation.FromQuaternion(back);

        // Front vector from recovered yaw must match the original heading.
        var expected = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);
        var got = new Vector3(-MathF.Sin(euler.Z), MathF.Cos(euler.Z), 0f);
        await Assert.That(Vector3.Dot(Vector3.Normalize(expected), Vector3.Normalize(got)))
            .IsGreaterThan(0.995f);
    }
}

using System.Numerics;

using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.World.Transform;

public class PositionAndRotationShortsTests
{
    [Test]
    public async Task ToRollPitchYawShorts_UnnormalizedSoutheast_MatchesPrincipalYaw()
    {
        // Movement used to plant +236.3 deg; doodad shorts without a W flip read that as ~+123 deg.
        var longArc = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, 236.3f * MathF.PI / 180f));
        var principal = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, -123.7f * MathF.PI / 180f));
        var a = PositionAndRotation.ToRollPitchYawShorts(longArc);
        var b = PositionAndRotation.ToRollPitchYawShorts(principal);
        await Assert.That(a.Item1).IsEqualTo(b.Item1);
        await Assert.That(a.Item2).IsEqualTo(b.Item2);
        await Assert.That(a.Item3).IsEqualTo(b.Item3);
        await Assert.That((int)a.Item3).IsLessThan(0); // SE short-arc Z is negative
    }

    [Test]
    public async Task ToRollPitchYawShorts_ForcesNonNegativeW()
    {
        var q = PositionAndRotation.ToQuaternion(new Vector3(0f, 0f, 225f * MathF.PI / 180f));
        await Assert.That(q.W).IsLessThan(0f);
        var (_, _, z) = PositionAndRotation.ToRollPitchYawShorts(q);
        // After W flip, Z = -sin(112.5°) < 0, same heading as principal -135°.
        await Assert.That((int)z).IsLessThan(0);
    }
}

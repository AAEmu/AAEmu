using System.Numerics;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

namespace AAEmu.UnitTests.Game.Physics;

/// <summary>
/// Pins down how Jitter's JVector/JQuaternion/JMatrix relate to System.Numerics, so moving code from Vector3 to
/// JVector does not silently change rotations or transforms.
/// Quaternions behave the same; matrices are transposed and compose in the opposite order.
/// </summary>
public class NumericsInteropTests
{
    private static readonly Quaternion A = Quaternion.Normalize(new Quaternion(0.2f, -0.3f, 0.5f, 0.8f));
    private static readonly Quaternion B = Quaternion.Normalize(new Quaternion(-0.4f, 0.1f, 0.3f, 0.6f));
    private static readonly Vector3 V = new(1f, 2f, 3f);

    private static async Task AssertSame(JVector actual, Vector3 expected)
    {
        await Assert.That(actual.X).IsEqualTo(expected.X).Within(0.0001f);
        await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(0.0001f);
        await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(0.0001f);
    }

    [Test]
    public async Task Transform_VectorByQuaternion_MatchesNumerics()
    {
        // Act
        var result = JVector.Transform(V, (JQuaternion)A);

        // Assert
        await AssertSame(result, Vector3.Transform(V, A));
    }

    [Test]
    public async Task Multiply_Quaternions_MatchesNumerics()
    {
        // Act
        var result = (JQuaternion)A * (JQuaternion)B;
        var expected = A * B;

        // Assert
        await Assert.That(result.X).IsEqualTo(expected.X).Within(0.0001f);
        await Assert.That(result.Y).IsEqualTo(expected.Y).Within(0.0001f);
        await Assert.That(result.Z).IsEqualTo(expected.Z).Within(0.0001f);
        await Assert.That(result.W).IsEqualTo(expected.W).Within(0.0001f);
    }

    [Test]
    public async Task CreateFromQuaternion_JMatrix_IsTransposeOfMatrix4x4()
    {
        // Act
        var jm = JMatrix.CreateFromQuaternion(A);
        var m = Matrix4x4.CreateFromQuaternion(A);

        // Assert
        await Assert.That(jm.M12).IsEqualTo(m.M21).Within(0.0001f);
        await Assert.That(jm.M21).IsEqualTo(m.M12).Within(0.0001f);
        await Assert.That(jm.M13).IsEqualTo(m.M31).Within(0.0001f);
        await Assert.That(jm.M31).IsEqualTo(m.M13).Within(0.0001f);
        await Assert.That(jm.M23).IsEqualTo(m.M32).Within(0.0001f);
        await Assert.That(jm.M32).IsEqualTo(m.M23).Within(0.0001f);
    }

    [Test]
    public async Task Transform_VectorByOwnRotationMatrix_MatchesNumerics()
    {
        // Each library applies its own matrix convention, so the rotated vector agrees.
        var result = JVector.Transform(V, JMatrix.CreateFromQuaternion(A));

        await AssertSame(result, Vector3.Transform(V, Matrix4x4.CreateFromQuaternion(A)));
    }

    [Test]
    public async Task Multiply_RotationMatrices_ComposesInReverseOrder()
    {
        // Numerics: m1 * m2 applies m1 first. Jitter: jm2 * jm1 applies jm1 first.
        var m1 = Matrix4x4.CreateFromQuaternion(A);
        var m2 = Matrix4x4.CreateFromQuaternion(B);
        var jm1 = JMatrix.CreateFromQuaternion(A);
        var jm2 = JMatrix.CreateFromQuaternion(B);

        var result = JVector.Transform(V, jm2 * jm1);

        await AssertSame(result, Vector3.Transform(V, m1 * m2));
    }

    [Test]
    public async Task ImplicitConversion_VectorAndQuaternion_RoundTrips()
    {
        // Act
        JVector jv = V;
        Vector3 back = jv;
        JQuaternion jq = A;
        Quaternion qBack = jq;

        // Assert
        await Assert.That(back).IsEqualTo(V);
        await Assert.That(qBack).IsEqualTo(A);
    }

    [Test]
    public async Task Multiply_TwoJVectors_IsDotProductNotComponentWise()
    {
        // Act
        var dot = (JVector)V * (JVector)V;

        // Assert: Vector3 * Vector3 would be (1, 4, 9); JVector * JVector is 1 + 4 + 9.
        await Assert.That(dot).IsEqualTo(14f);
    }

    [Test]
    public async Task Distance_JVector_MatchesVector3Distance()
    {
        // Arrange
        var other = new Vector3(-4f, 0.5f, 7f);

        // Act
        var distance = ((JVector)V).Distance(other);
        var distanceSquared = ((JVector)V).DistanceSquared(other);

        // Assert
        await Assert.That(distance).IsEqualTo(Vector3.Distance(V, other)).Within(0.0001f);
        await Assert.That(distanceSquared).IsEqualTo(Vector3.DistanceSquared(V, other)).Within(0.0001f);
    }
}

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Physics;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

namespace AAEmu.UnitTests.Game.Physics;

/// <summary>
/// Tests that ship hulls live in the game's axes (Z up): length on local +Y (bow), beam on +X, height on +Z.
/// </summary>
public class ShipControllerTests
{
    /// <summary>
    /// Creates a hull in <paramref name="world"/>. ShipController's finalizer removes the hull from its world, which must
    /// not run after the test disposes that world (Jitter keeps body data in unmanaged memory), so it is suppressed here.
    /// </summary>
    private static ShipController CreateShip(Jitter2.World world, float yaw = 0f)
    {
        var controller = new ShipController(world, CreateShipModel());
        GC.SuppressFinalize(controller);
        controller.Build(new JVector(100f, 200f, 50f), JQuaternion.CreateRotationZ(yaw));
        return controller;
    }

    private static ShipModelV1 CreateShipModel() => new()
    {
        Mass = 1000f,
        MassBoxSizeX = 2f,  // beam
        MassBoxSizeY = 10f, // length
        MassBoxSizeZ = 3f,  // height
    };

    [Test]
    [Arguments(0f)]
    [Arguments(45f)]
    [Arguments(90f)]
    [Arguments(-120f)]
    public async Task Build_WithYaw_HullLengthAxisPointsAlongGameForward(float yawDeg)
    {
        // Arrange
        using var world = new Jitter2.World();
        var yaw = yawDeg.DegToRad();

        // Act
        var controller = CreateShip(world, yaw);
        var rotation = JMatrix.CreateFromQuaternion(controller.Hull.Orientation);
        var lengthAxis = JVector.Transform(new JVector(0f, 1f, 0f), rotation);
        var upAxis = JVector.Transform(new JVector(0f, 0f, 1f), rotation);

        // Assert
        var (forwardX, forwardY) = PhysicsUtil.GetForward(yaw);
        await Assert.That(lengthAxis.X).IsEqualTo(forwardX).Within(0.0001f);
        await Assert.That(lengthAxis.Y).IsEqualTo(forwardY).Within(0.0001f);
        await Assert.That(lengthAxis.Z).IsEqualTo(0f).Within(0.0001f);
        await Assert.That(upAxis.Z).IsEqualTo(1f).Within(0.0001f);
    }

    [Test]
    public async Task Build_WithZeroYaw_BoundingBoxIsLongOnYAndTallOnZ()
    {
        // Arrange
        using var world = new Jitter2.World();

        // Act
        var controller = CreateShip(world);
        world.Step(0.01f, false);
        var box = controller.Hull.Shapes[0].WorldBoundingBox;

        // Assert
        await Assert.That(box.Max.X - box.Min.X).IsEqualTo(2f).Within(0.1f);
        await Assert.That(box.Max.Y - box.Min.Y).IsEqualTo(10f).Within(0.1f);
        await Assert.That(box.Max.Z - box.Min.Z).IsEqualTo(3f).Within(0.1f);
    }

    [Test]
    public async Task Hull_WithNegativeAngularVelocityAboutZ_DecreasesYawAndStaysLevel()
    {
        // Arrange: same convention as ShipController.ApplyForceAndTorque (AngularVelocity = (0, 0, -RotSpeed))
        using var world = new Jitter2.World();
        world.Gravity = JVector.Zero;
        var controller = CreateShip(world);
        controller.Hull.Damping = (0f, 0f);

        // Act
        for (var i = 0; i < 10; i++)
        {
            controller.Hull.AngularVelocity = new JVector(0f, 0f, -0.5f);
            world.Step(0.1f, false);
        }

        var rotation = PhysicsUtil.GetGameRotation(controller.Hull.Orientation);

        // Assert
        await Assert.That(rotation.Z).IsEqualTo(-0.5f).Within(0.02f);
        await Assert.That(rotation.X).IsEqualTo(0f).Within(0.0001f);
        await Assert.That(rotation.Y).IsEqualTo(0f).Within(0.0001f);
    }

    [Test]
    public async Task Hull_WithZUpGravity_FallsAlongNegativeZ()
    {
        // Arrange
        using var world = new Jitter2.World();
        world.Gravity = new JVector(0f, 0f, -9.81f);
        var controller = CreateShip(world);

        // Act
        for (var i = 0; i < 10; i++)
            world.Step(0.01f, false);

        // Assert
        await Assert.That(controller.Hull.Position.Z).IsLessThan(50f);
        await Assert.That(controller.Hull.Position.X).IsEqualTo(100f).Within(0.0001f);
        await Assert.That(controller.Hull.Position.Y).IsEqualTo(200f).Within(0.0001f);
    }

    [Test]
    public async Task Build_WhenCalled_HullHasInertiaOnlyAboutZ()
    {
        // Arrange
        using var world = new Jitter2.World();

        // Act
        var controller = CreateShip(world);
        var inverseInertia = controller.Hull.InverseInertia;

        // Assert
        await Assert.That(inverseInertia.M11).IsEqualTo(0f);
        await Assert.That(inverseInertia.M22).IsEqualTo(0f);
        await Assert.That(inverseInertia.M33).IsGreaterThan(0f);
    }

    [Test]
    public async Task Hull_WithUpwardForceAtBow_StaysLevel()
    {
        // Arrange: a push like this would pitch a free box nose-up
        using var world = new Jitter2.World();
        world.Gravity = JVector.Zero;
        var controller = CreateShip(world);
        var bow = controller.Hull.Position + new JVector(0f, 4f, 0f);

        // Act
        for (var i = 0; i < 10; i++)
        {
            controller.Hull.AddForce(new JVector(0f, 0f, 50000f), bow);
            world.Step(0.01f, false);
        }

        var rotation = PhysicsUtil.GetGameRotation(controller.Hull.Orientation);

        // Assert
        await Assert.That(rotation.X).IsEqualTo(0f).Within(0.0001f);
        await Assert.That(rotation.Y).IsEqualTo(0f).Within(0.0001f);
        await Assert.That(controller.Hull.Position.Z).IsGreaterThan(50f);
    }

    [Test]
    public async Task Hull_WithSidewaysForceAtBow_StillYaws()
    {
        // Arrange
        using var world = new Jitter2.World();
        world.Gravity = JVector.Zero;
        var controller = CreateShip(world);
        var bow = controller.Hull.Position + new JVector(0f, 4f, 0f);

        // Act: push the bow toward +X, which turns the ship toward +X (negative yaw)
        for (var i = 0; i < 10; i++)
        {
            controller.Hull.AddForce(new JVector(50000f, 0f, 0f), bow);
            world.Step(0.01f, false);
        }

        var rotation = PhysicsUtil.GetGameRotation(controller.Hull.Orientation);

        // Assert
        await Assert.That(rotation.Z).IsLessThan(-0.001f);
        await Assert.That(rotation.X).IsEqualTo(0f).Within(0.0001f);
        await Assert.That(rotation.Y).IsEqualTo(0f).Within(0.0001f);
    }

    [Test]
    [Arguments(0f, 0f, 2f)]
    [Arguments(90f, -2f, 0f)]
    [Arguments(180f, 0f, -2f)]
    public async Task GetMassBoxCenterXy_WithBowOffset_RotatesOffsetWithHeading(float yawDeg, float expectedDx, float expectedDy)
    {
        // Arrange: mass center 2 m toward the bow (+Y in hull space)
        using var world = new Jitter2.World();
        var model = CreateShipModel();
        model.MassCenterY = 2f;
        var controller = new ShipController(world, model);
        GC.SuppressFinalize(controller);
        controller.Build(new JVector(100f, 200f, 50f), JQuaternion.CreateRotationZ(yawDeg.DegToRad()));

        // Act
        ShipShipInteraction.GetMassBoxCenterXy(controller.Hull, model, 1f, out var cx, out var cy);

        // Assert
        await Assert.That(cx - 100f).IsEqualTo(expectedDx).Within(0.0001f);
        await Assert.That(cy - 200f).IsEqualTo(expectedDy).Within(0.0001f);
    }
}

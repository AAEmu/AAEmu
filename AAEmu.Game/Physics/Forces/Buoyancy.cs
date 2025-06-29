using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Forces;

/// <summary>
/// Simple Helper that adds buoyancy forces to a body if it is within
/// the FluidVolume. The volume is represented by an axis aligned bounding box or by
/// the user.
/// </summary>
public class Buoyancy : ForceGenerator
{

    /// <summary>
    /// Returns true if the given point is within the area.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>True if the given point is within the area.</returns>
    public delegate bool DefineFluidArea(ref JVector point);

    private readonly Dictionary<Shape, JVector[]> _samples = [];
    private readonly List<RigidBody> _bodies = [];

    /// <summary>
    /// The axis aligned bounding box representing the fluid.
    /// </summary>
    public JBoundingBox FluidBox { get; set; }

    /// <summary>
    /// Density of the fluid. Default is 2.0.
    /// </summary>
    public float Density { get; set; }

    /// <summary>
    /// Damping applied to the body if it is in contact with the fluid.
    /// Default is 0.1.
    /// </summary>
    public float Damping { get; set; }

    /// <summary>
    /// Flow direction and magnitude.
    /// </summary>
    public JVector Flow { get; set; }

    private DefineFluidArea _fluidArea;
    private float WaterSurfaceLevel => FluidBox.Max.Y;

    /// <summary>
    /// Creates a new instance of the FluidVolume class.
    /// </summary>
    /// <param name="world">The world.</param>
    public Buoyancy(World world) : base(world)
    {
        Density = 1.025f; // 1025 кг/м³ (seawater density)
        Damping = 0.1f;
        Flow = JVector.Zero;
    }

    /// <summary>
    /// Removes bodies from the fluid.
    /// </summary>
    /// <param name="body"></param>
    public void Remove(RigidBody body)
    {
        var flag = false;

        foreach (var b in _bodies)
        {
            if (body.Shapes[0] == b.Shapes[0])
            {
                flag = true;
                break;
            }
        }

        _bodies.Remove(body);
        if (!flag) _samples.Remove(body.Shapes[0]);
    }

    /// <summary>
    /// Removes all bodies from the fluid.
    /// </summary>
    public void Clear()
    {
        _bodies.Clear();
        _samples.Clear();
    }

    /// <summary>
    /// If you don't want to use the default axis aligned bounding box as
    /// fluid area representation you can define your own area using the FluidAreaDelegate.
    /// </summary>
    /// <param name="fluidArea">A delegate specifying the fluid area. Set to null if you
    /// want to use the default box.</param>
    public void UseOwnFluidArea(DefineFluidArea fluidArea)
    {
        _fluidArea = fluidArea;
    }

    /// <summary>
    /// Adds a body to the fluid. Only bodies which where added
    /// to the fluid volume gets affected by buoyancy forces.
    /// </summary>
    /// <param name="body">The body which should be added.</param>
    /// <param name="subdivisions">The object is subdivided in smaller objects
    /// for which buoyancy force is calculated. The more subdivisions the better
    /// the results. Note that the total number of subdivisions is subdivisions³.</param>
    public void Add(RigidBody body, int subdivisions)
    {
        List<JVector> massPoints = [];

        var diff = body.Shapes[0].WorldBoundingBox.Max - body.Shapes[0].WorldBoundingBox.Min;

        if (MathHelper.CloseToZero(diff))
            throw new InvalidOperationException("BoundingBox volume of the shape is zero.");

        for (var i = 0; i < subdivisions; i++)
        {
            for (var e = 0; e < subdivisions; e++)
            {
                for (var k = 0; k < subdivisions; k++)
                {
                    JVector testVector;
                    testVector.X = body.Shapes[0].WorldBoundingBox.Min.X + (diff.X / (subdivisions - 1)) * i;
                    testVector.Y = body.Shapes[0].WorldBoundingBox.Min.Y + (diff.Y / (subdivisions - 1)) * e;
                    testVector.Z = body.Shapes[0].WorldBoundingBox.Min.Z + (diff.Z / (subdivisions - 1)) * k;

                    if (NarrowPhase.PointTest(body.Shapes[0], in testVector))
                    {
                        massPoints.Add(testVector);
                    }
                }
            }
        }

        _samples.Add(body.Shapes[0], massPoints.ToArray());
        _bodies.Add(body);
    }

    public void AddForRectangularParallelepiped(RigidBody body, int subdivisions)
    {
        if (body.Shapes.Count == 0)
            throw new ArgumentException("body has no shapes.");

        var shape = body.Shapes[0];
        var bbox = shape.WorldBoundingBox;
        var min = bbox.Min;
        var max = bbox.Max;

        // Dimensions of the parallelepiped
        var size = max - min;

        if (MathHelper.CloseToZero(size))
            throw new InvalidOperationException("BoundingBox volume is zero.");

        var massPoints = new List<JVector>();

        // Step between points on each axis
        var stepX = size.X / subdivisions;
        var stepY = size.Y / subdivisions;
        var stepZ = size.Z / subdivisions;

        // Generating points inside a parallelepiped
        for (var i = 0; i < subdivisions; i++)
        {
            for (var j = 0; j < subdivisions; j++)
            {
                for (var k = 0; k < subdivisions; k++)
                {
                    // Current point coordinates
                    var x = min.X + (i + 0.5f) * stepX;
                    var y = min.Y + (j + 0.5f) * stepY;
                    var z = min.Z + (k + 0.5f) * stepZ;

                    var point = new JVector(x, y, z);

                    // For a parallelepiped, all points inside the BoundingBox are considered to belong to the body
                    massPoints.Add(point);
                }
            }
        }

        // Save points
        _samples.Add(shape, massPoints.ToArray());
        _bodies.Add(body);
    }

    public override void PreStep(float timeStep)
    {
        foreach (var body in _bodies.ToArray())
        {
            if (body.IsStatic || !body.IsActive) continue;

            var slave = (Slave)body.Tag;
            if (slave == null) continue;

            // Skip if no controller or mass
            if (slave.ShipController == null || slave.ShipController.ShipModel.Mass <= 0)
                continue;
            
            // Skip simulation if still summoning
            body.AffectedByGravity = slave.SpawnTime.AddSeconds(slave.Template.PortalTime) <= DateTime.UtcNow;
            if (!body.AffectedByGravity)
            {
                continue;
            }

            var waterSurfaceLevel = WaterSurfaceLevel;
            var centerPosition = body.Position;
            if (_fluidArea != null && _fluidArea(ref centerPosition))
            {
                waterSurfaceLevel = slave.CachedWaterSurface;
            }

            var depth = waterSurfaceLevel - body.Position.Y;
            if (depth <= 0) continue;
            
            ApplyDrag(body, slave.ShipController.ShipModel.MassBoxSizeX, slave.ShipController.ShipModel.MassBoxSizeY, slave.ShipController.ShipModel.MassBoxSizeZ);
            // Calculate submerged depth and buoyancy force
            var submergedDepth = Math.Max(0, waterSurfaceLevel - body.Position.Y);
            var isOnWater = submergedDepth > 0;

            if (isOnWater)
            {
                // Apply buoyancy and drag forces
                var buoyancyForce = new JVector(0, submergedDepth * body.Mass * Density * 9.81f, 0);
                body.AddForce(buoyancyForce);

                var dragForce = new JVector(-body.Velocity.X * Density, -body.Velocity.Y * Density, -body.Velocity.Z * Density);
                body.AddForce(dragForce);
            }
        }
    }

    private void ApplyDrag(RigidBody body, float hullWidth, float hullLength, float hullHeight)
    {
        var velocity = body.Velocity;
        var speed = velocity.Length();
        if (speed < 0.1f) return;

        const float DragCoefficient = 0.8f;
        var area = hullWidth * hullHeight;
        var drag = 0.5f * Density * DragCoefficient * area * speed * speed;
        JVector.NormalizeInPlace(ref velocity);
        JVector.NegateInPlace(ref velocity);
        velocity *= drag;
        body.AddForce(velocity);
    }
}

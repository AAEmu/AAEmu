using System;
using System.Collections.Generic;
using Jitter;
using Jitter.Collision;
using Jitter.Collision.Shapes;
using Jitter.Dynamics;
using Jitter.LinearMath;

namespace AAEmu.Game.Physics.Forces
{
    /// <summary>
    /// Simple Helper that adds buoyancy forces to a body if it is within
    /// the FluidVolume. The volume is represented by a axis aligned bounding box or by
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

        private Dictionary<Shape, JVector[]> samples = new Dictionary<Shape, JVector[]>();
        private List<RigidBody> bodies = new List<RigidBody>();

        /// <summary>
        /// The axis aligned bounding box representing the fluid.
        /// </summary>
        public JBBox FluidBox { get; set; }

        /// <summary>
        /// Densitity of the fluid. Default is 2.0.
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

        private DefineFluidArea fluidArea = null;

        /// <summary>
        /// Creates a new instance of the FluidVolume class.
        /// </summary>
        /// <param name="world">The world.</param>
        public Buoyancy(World world)
            : base(world)
        {
            Density = 2.0f;
            Damping = 0.1f;
            Flow = JVector.Zero;
        }

        /// <summary>
        /// Removes bodies from the fluid.
        /// </summary>
        /// <param name="body"></param>
        public void Remove(RigidBody body)
        {
            bool flag = false;

            foreach (RigidBody b in bodies)
            {
                if (body.Shape == b.Shape)
                { flag = true; break; }
            }

            bodies.Remove(body);
            if (!flag) samples.Remove(body.Shape);
        }

        /// <summary>
        /// Removes all bodies from the fluid.
        /// </summary>
        public void Clear()
        {
            bodies.Clear();
            samples.Clear();
        }

        /// <summary>
        /// If you don't want to use the default axis aligned bounding box as
        /// fluid area representation you can define your own area using the FluidAreaDelegate.
        /// </summary>
        /// <param name="fluidArea">A delegate specifing the fluid area. Set to null if you
        /// want to use the default box.</param>
        public void UseOwnFluidArea(DefineFluidArea fluidArea)
        {
            this.fluidArea = fluidArea;
        }

        /// <summary>
        /// Adds a body to the fluid. Only bodies which where added
        /// to the fluidvolume gets affected by buoyancy forces.
        /// </summary>
        /// <param name="body">The body which should be added.</param>
        /// <param name="subdivisions">The object is subdivided in smaller objects
        /// for which buoyancy force is calculated. The more subdivisons the better
        /// the results. Note that the total number of subdivisions is subdivisions³.</param>
        public void Add(RigidBody body, int subdivisions)
        {
            List<JVector> massPoints = new List<JVector>();
            JVector testVector;

            JVector diff = body.Shape.BoundingBox.Max - body.Shape.BoundingBox.Min;

            if (diff.IsNearlyZero())
                throw new InvalidOperationException("BoundingBox volume of the shape is zero.");

            Multishape ms = body.Shape as Multishape;
            int values = 0;

            if (ms != null)
            {
                JBBox largeBox = JBBox.LargeBox;
                values = ms.Prepare(ref largeBox);
            }

            for (int i = 0; i < subdivisions; i++)
            {
                for (int e = 0; e < subdivisions; e++)
                {
                    for (int k = 0; k < subdivisions; k++)
                    {
                        testVector.X = body.Shape.BoundingBox.Min.X + (diff.X / (float)(subdivisions - 1)) * ((float)i);
                        testVector.Y = body.Shape.BoundingBox.Min.Y + (diff.Y / (float)(subdivisions - 1)) * ((float)e);
                        testVector.Z = body.Shape.BoundingBox.Min.Z + (diff.Z / (float)(subdivisions - 1)) * ((float)k);

                        JMatrix ident = JMatrix.Identity;
                        JVector zero = JVector.Zero;

                        if (ms != null)
                        {
     
                            for (int j = 0; j < values; j++)
                            {
                                ms.SetCurrentShape(j);

               
                                if (GJKCollide.Pointcast(body.Shape, ref ident,
                                    ref zero, ref testVector))
                                {
                                    massPoints.Add(testVector);
                                }
                            }
                        }
                        else
                        {
                            if (GJKCollide.Pointcast(body.Shape,ref ident,
                                ref zero, ref testVector))
                            {
                                massPoints.Add(testVector);
                            }
                        }
                    }
                }
            }

            samples.Add(body.Shape, massPoints.ToArray());
            bodies.Add(body);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="timeStep"></param>
        public override void PreStep(float timeStep)
        {
            float damping = (float)Math.Pow(Damping, timeStep);

            foreach (RigidBody body in bodies)
            {

                if (FluidBox.Contains(body.BoundingBox) != JBBox.ContainmentType.Disjoint)
                {
                    JVector[] positions = samples[body.Shape];

                    float frac = 0.0f;

                    JVector currentCoord = JVector.Zero;
                    for (int i = 0; i < positions.Length; i++)
                    {
                        currentCoord = JVector.Transform(positions[i], body.Orientation);
                        currentCoord = JVector.Add(currentCoord, body.Position);

                        bool containsCoord = false;

                        if (fluidArea == null) containsCoord = FluidBox.Contains(ref currentCoord) != JBBox.ContainmentType.Disjoint;
                        else containsCoord = fluidArea(ref currentCoord);

                        if (containsCoord)
                        {
                            body.AddForce((1.0f / positions.Length) * body.Mass * Flow);
                            body.AddForce(-(1.0f / positions.Length) * body.Shape.Mass * Density * world.Gravity, currentCoord);
                            frac += 1.0f / positions.Length;
                        }
                    }

                    body.AngularVelocity *= damping;
                    body.LinearVelocity *= damping;
                }


            }
        }

    }
}

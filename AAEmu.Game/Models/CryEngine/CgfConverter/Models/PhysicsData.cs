using System.Numerics;

namespace CgfConverter
{
    public struct PhysicsData
    {
        // Collision or hitbox info.  Part of the MeshPhysicsData chunk
        public int Unknown4;
        public int Unknown5;
        public float[] Unknown6;  // array length 3, Inertia?
        public Quaternion Rot;  // Most definitely a quaternion. Probably describes rotation of the physics object.
        public Vector3 Center;  // Center, or position. Probably describes translation of the physics object. Often corresponds to the center of the mesh data as described in the submesh chunk.
        public float Unknown10; // Mass?
        public int Unknown11;
        public int Unknown12;
        public float Unknown13;
        public float Unknown14;
        public PhysicsPrimitiveType PrimitiveType;
        public PhysicsCube Cube;  // Primitive Type 0
        public PhysicsPolyhedron PolyHedron;  // Primitive Type 1
        public PhysicsCylinder Cylinder; // Primitive Type 5
        public PhysicsShape6 UnknownShape6;  // Primitive Type 6
    }
}

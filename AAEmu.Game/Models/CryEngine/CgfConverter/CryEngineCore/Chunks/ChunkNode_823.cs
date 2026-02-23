using Extensions;
using System;
using System.IO;
using System.Numerics;

namespace CgfConverter.CryEngineCore;

internal sealed class ChunkNode_823 : ChunkNode
{
    public override void Read(BinaryReader b)
    {
        base.Read(b);

        Name = b.ReadFString(64);
        if (string.IsNullOrEmpty(Name))
            Name = "unknown";

        ObjectNodeID = b.ReadInt32(); // Object reference ID
        ParentNodeID = b.ReadInt32();
        NumChildren = b.ReadInt32();
        MaterialID = b.ReadInt32();  // Material ID?
        SkipBytes(b, 4);

        // Read the 4x4 transform matrix.
        var transform = new Matrix4x4
        {
            M11 = b.ReadSingle(),
            M12 = b.ReadSingle(),
            M13 = b.ReadSingle(),
            M14 = b.ReadSingle(),
            M21 = b.ReadSingle(),
            M22 = b.ReadSingle(),
            M23 = b.ReadSingle(),
            M24 = b.ReadSingle(),
            M31 = b.ReadSingle(),
            M32 = b.ReadSingle(),
            M33 = b.ReadSingle(),
            M34 = b.ReadSingle(),
            M41 = b.ReadSingle() * VERTEX_SCALE,
            M42 = b.ReadSingle() * VERTEX_SCALE,
            M43 = b.ReadSingle() * VERTEX_SCALE,
            M44 = b.ReadSingle(),
        };

        //transform.M14 = transform.M24 = transform.M34 = 0f;
        //transform.M44 = 1f;
        Transform = transform;

        Pos = b.ReadVector3() * VERTEX_SCALE;   // Obsolete
        Rot = b.ReadQuaternion();               // Obsolete
        Scale = b.ReadVector3();                // Obsolete

        // read the controller pos/rot/scale
        PosCtrlID = b.ReadInt32();
        RotCtrlID = b.ReadInt32();
        SclCtrlID = b.ReadInt32();

        PropertyStringLength = b.ReadInt32();
    }
}

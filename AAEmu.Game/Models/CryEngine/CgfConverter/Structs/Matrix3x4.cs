using Extensions;
using System;
using System.Numerics;

namespace CgfConverter.Structs;

/// <summary>
/// WorldToBone and BoneToWorld objects in Cryengine files.  Inspiration/code based from
/// https://referencesource.microsoft.com/#System.Numerics/System/Numerics/Matrix4x4.cs,48ce53b7e55d0436
/// </summary>
public struct Matrix3x4 : IEquatable<Matrix3x4>
{
    public float M11;
    public float M12;
    public float M13;
    public float M14;
    public float M21;
    public float M22;
    public float M23;
    public float M24;
    public float M31;
    public float M32;
    public float M33;
    public float M34;

    public Matrix3x4(float m11, float m12, float m13, float m14,
             float m21, float m22, float m23, float m24,
             float m31, float m32, float m33, float m34)
    {
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M14 = m14;

        M21 = m21;
        M22 = m22;
        M23 = m23;
        M24 = m24;

        M31 = m31;
        M32 = m32;
        M33 = m33;
        M34 = m34;
    }

    public Vector3 Translation { 
        get { return new Vector3(M14, M24, M34); }
        set 
        {
            M14 = value.X;
            M24 = value.Y;
            M34 = value.Z;
        }
    }

    public Matrix3x3 Rotation
    {
        get {
            return new Matrix3x3(M11, M12, M13, M21, M22, M23, M31, M32, M33);
        }
    }

    /// <summary>
    /// Creates a rotation matrix from the given Quaternion rotation value.
    /// </summary>
    /// <param name="quaternion">The source Quaternion.</param>
    /// <returns>The rotation matrix.</returns>
    public static Matrix3x4 CreateFromQuaternion(Quaternion quaternion)
    {
        var rot = quaternion.ConvertToRotationMatrix();
        return new Matrix3x4()
        {
            M11 = rot.M11,
            M12 = rot.M12,
            M13 = rot.M13,
            M14 = 0,
            M21 = rot.M21,
            M22 = rot.M22,
            M23 = rot.M23,
            M24 = 0,
            M31 = rot.M31,
            M32 = rot.M32,
            M33 = rot.M33,
            M34 = 0
        };
    }

    public static Matrix3x4 CreateFromParts(Quaternion quaternion, Vector3 translation)
    {
        Matrix3x4 result = CreateFromQuaternion(quaternion);
        result.Translation = translation;

        return result;
    }

    public readonly Matrix4x4 ConvertToTransformMatrix()
    {
        var m = new Matrix4x4
        {
            M11 = M11,
            M12 = M12,
            M13 = M13,
            M14 = M14,
            M21 = M21,
            M22 = M22,
            M23 = M23,
            M24 = M24,
            M31 = M31,
            M32 = M32,
            M33 = M33,
            M34 = M34,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1
        };
        return m;
    }

    /// <summary>
    /// Returns a boolean indicating whether this matrix instance is equal to the other given matrix.
    /// </summary>
    /// <param name="other">The matrix to compare this instance to.</param>
    /// <returns>True if the matrices are equal; False otherwise.</returns>
    public bool Equals(Matrix3x4 other)
    {
        return (M11 == other.M11 && M22 == other.M22 && M33 == other.M33 && // Check diagonal element first for early out.
                M12 == other.M12 && M13 == other.M13 && M14 == other.M14 &&
                M21 == other.M21 && M23 == other.M23 && M24 == other.M24 &&
                M31 == other.M31 && M32 == other.M32 && M34 == other.M34);
    }
}

using System;
using CgfConverter.Structs;
using System.Numerics;

namespace Extensions;

public static class Matrix4x4Extensions    // Extensions for System.Numerics Matrix4x4
{
    /// <summary>
    /// Gets the Rotation portion of a Transform Matrix4x4 (upper left).
    /// </summary>
    /// <returns>New Matrix33 with the rotation component.</returns>
    public static Matrix3x3 GetRotation(this Matrix4x4 matrix)
    {
        return new Matrix3x3(
            m11: matrix.M11,
            m12: matrix.M12,
            m13: matrix.M13,
            m21: matrix.M21,
            m22: matrix.M22,
            m23: matrix.M23,
            m31: matrix.M31,
            m32: matrix.M32,
            m33: matrix.M33
        );
    }

    public static Vector3 GetScale(this Matrix4x4 matrix)
    {
        return new Vector3(
            x: matrix.M41,
            y: matrix.M42,
            z: matrix.M43
        );
    }

    public static Vector3 GetTranslation(this Matrix4x4 m)
    {
        return new Vector3(
            x: m.M14,
            y: m.M24,
            z: m.M34
        );
    }

    public static Matrix4x4 CreateFromMatrix3x4(Matrix3x4 matrix)
    {
        return new Matrix4x4(
            m11: matrix.M11,
            m12: matrix.M12,
            m13: matrix.M13,
            m14: matrix.M14,
            m21: matrix.M21,
            m22: matrix.M22,
            m23: matrix.M23,
            m24: matrix.M24,
            m31: matrix.M31,
            m32: matrix.M32,
            m33: matrix.M33,
            m34: matrix.M34,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
    }

    public static Matrix4x4 CreateFromRotationMatrix(Matrix3x3 m3)
    {
        return new Matrix4x4(
            m11: m3.M11,
            m12: m3.M12,
            m13: m3.M13,
            m14: 0.0f,
            m21: m3.M21,
            m22: m3.M22,
            m23: m3.M23,
            m24: 0.0f,
            m31: m3.M31,
            m32: m3.M32,
            m33: m3.M33,
            m34: 0.0f,
            m41: 0.0f,
            m42: 0.0f,
            m43: 0.0f,
            m44: 1.0f
        );
    }

    public static Matrix4x4 CreateLocalTransformFromB2W(Matrix4x4 parent, Matrix4x4 child)
    {
        return parent * child;
    }

    public static Matrix4x4 CreateTransformFromParts(Vector3 translation, Matrix3x3 rotation)
    {
        return new Matrix4x4(
            m11: rotation.M11,
            m12: rotation.M12,
            m13: rotation.M13,
            m14: translation.X,
            m21: rotation.M21,
            m22: rotation.M22,
            m23: rotation.M23,
            m24: translation.Y,
            m31: rotation.M31,
            m32: rotation.M32,
            m33: rotation.M33,
            m34: translation.Z,
            m41: 0.0f,
            m42: 0.0f,
            m43: 0.0f,
            m44: 1.0f
        );
    }

    public static Matrix4x4 CreateOrientation(Vector3 dir, Vector3 up, float rollAngle)
    {
        if (dir.Length() == 0f)
            return Matrix4x4.Identity;

        var yAxis = Vector3.Normalize(dir);

        if (yAxis is {X: 0, Y: 0} && up == Vector3.UnitZ)
            up = new Vector3(-yAxis.Z, 0, 0);

        var xAxis = Vector3.Normalize(Vector3.Cross(up, yAxis));
        var zAxis = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));

        var tm = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0,
            yAxis.X, yAxis.Y, yAxis.Z, 0,
            zAxis.X, zAxis.Y, zAxis.Z, 0,
            0, 0, 0, 1);

        if (rollAngle != 0)
            tm *= Matrix4x4.CreateRotationY(rollAngle);
        
        return tm;
    }

    public static Matrix4x4 CreateFromAngles(float x, float y, float z)
    {
        var (sx, cx) = Math.SinCos(x);
        var (sy, cy) = Math.SinCos(y);
        var (sz, cz) = Math.SinCos(z);
        var sycz = sy * cz;
        var sysz = sy * sz;
        return new Matrix4x4
        {
            M11 = (float)(cy * cz),
            M12 = (float)(sycz * sx - cx * sz),
            M13 = (float)(sycz * cx + sx * sz),
            M14 = 0,
            M21 = (float)(cy * sz),
            M22 = (float)(sysz * sx + cx * cz),
            M23 = (float)(sysz * cx - sx * cz),
            M24 = 0,
            M31 = (float)-sy,
            M32 = (float)(cy * sx),
            M33 = (float)(cy * cx),
            M34 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
    }

    public static Vector3 ToTranslationVector(this Matrix4x4 matrix)
    {
        // Extracts the translation component from the 4x4 matrix.
        return new Vector3(matrix.M14, matrix.M24, matrix.M34);
    }

    public static Vector4 ToAxisAngleDegrees(this Matrix4x4 matrix)
    {
        Quaternion quaternion = Quaternion.CreateFromRotationMatrix(matrix);
        return quaternion.ToAxisAngle();
    }
}

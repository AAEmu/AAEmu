namespace CgfConverter.Structs;

/// <summary>Minimal layout matching CryEngine object.dat reads (see <see cref="AAEmu.Game.Models.CryEngine.Objects.ObjectDataBase"/>).</summary>
public struct Matrix3x4
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
}

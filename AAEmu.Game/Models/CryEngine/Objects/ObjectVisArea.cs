using System.Numerics;
using System.Text;

namespace AAEmu.Game.Models.CryEngine.Objects;

public class ObjectVisArea
{
    public uint ObjectType { get; set; }
    public Vector3 StartPos { get; set; } = Vector3.Zero;
    public Vector3 EndPos { get; set; } = Vector3.Zero;
    public Vector3 StartPos2 { get; set; } = Vector3.Zero;
    public Vector3 EndPos2 { get; set; } = Vector3.Zero;
    private byte[] NameData { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public int DataSize { get; set; }
    public byte[] VisData { get; set; } = [];
    public uint VectorCount { get; internal set; }

    public bool Contains(Vector3 point, bool ignoreZ = false)
    {
        return point.X >= Math.Min(StartPos.X, EndPos.X) && point.X <= Math.Max(StartPos.X, EndPos.X) &&
               point.Y >= Math.Min(StartPos.Y, EndPos.Y) && point.Y <= Math.Max(StartPos.Y, EndPos.Y) &&
               (ignoreZ || (point.Z >= Math.Min(StartPos.Z, EndPos.Z) && point.Z <= Math.Max(StartPos.Z, EndPos.Z)));
    }

    public bool Contains(Vector2 point)
    {
        return point.X >= Math.Min(StartPos.X, EndPos.X) && point.X <= Math.Max(StartPos.X, EndPos.X) &&
               point.Y >= Math.Min(StartPos.Y, EndPos.Y) && point.Y <= Math.Max(StartPos.Y, EndPos.Y);
    }

    public void ReadName(BinaryReader br, int dataLength)
    {
        NameData = br.ReadBytes(dataLength);
        var nullpos = Array.IndexOf(NameData, (byte)0);
        if (nullpos >= 0)
        {
            try
            {
                Name = Encoding.UTF8.GetString(NameData[0..nullpos]);
            }
            catch
            {
                Name = "<ERROR>";
            }
        }
        else
        {
            Name = string.Empty;
        }
    }
}

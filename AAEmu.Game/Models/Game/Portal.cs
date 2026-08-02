using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game;

public class Portal : PacketMarshaler
{
    /// <summary>
    /// Wire <c>id</c>. For return-district book entries this is the <c>district_id</c>;
    /// for private (recorded) portals it is the private-book row id.
    /// </summary>
    public uint Id { get; set; }

    public string Name { get; set; }

    /// <summary>
    /// <c>return_point_id</c>; private portals leave it 0.
    /// </summary>
    public uint Type { get; set; }

    public uint ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float ZRot { get; set; }
    public float Yaw { get; set; }

    public uint SubZoneId { get; set; }
    public uint Owner { get; set; }
    public uint WorldId { get; set; }

    public bool IsFavorite { get; set; }
    public bool IsDisable { get; set; }
    public byte FactionPermission { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        // u32 id, string name, u32 type, u32 zoneId, f32×3 pos, f32 zRot,
        // u8 isFavorite, u8 isDisable, u8 factionPermission.
        stream.Write(Id);
        stream.Write(Name ?? string.Empty); // max 128 on client
        stream.Write(Type);
        stream.Write(ZoneId);
        var origin = ZoneId != 0 ? ZoneManager.Instance.GetZoneOriginCell(ZoneId) : Vector2.Zero;
        stream.Write(X - origin.X * 1024f);
        stream.Write(Y - origin.Y * 1024f);
        stream.Write(Z);
        stream.Write(ZRot);
        stream.Write(IsFavorite);
        stream.Write(IsDisable);
        stream.Write(FactionPermission);
        return stream;
    }
}

public class VisitedDistrict
{
    public uint Id { get; set; }
    public uint SubZone { get; set; }
    public uint Owner { get; set; }
}

public class DistrictReturnPoints
{
    public uint Id { get; set; }
    public uint DistrictId { get; set; }
    public FactionsEnum FactionId { get; set; }
    public uint ReturnPointId { get; set; }
}

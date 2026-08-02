using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Wire (minimal 78 B): u32 sid, u32 sType, u8 mIdx, u8 pIdx, u16 tIdx, u32 templateId,
/// u32 e040type, u32 grpId, u8 grpMemIdx, f32 x,y,z, f32 zRot, f32 scale, then zeroed tail.
/// Optional ISerialize groups are written without a presence byte on this path (always present).
/// Zone does not put a client bcId in this packet — World assigns one.
/// </summary>
public sealed class ZwSpawnNpcParsed
{
    public uint SpawnerId { get; init; }
    public uint SpawnerType { get; init; }
    public byte MemberIdx { get; init; }
    public byte PartIdx { get; init; }
    public ushort TableIdx { get; init; }
    public uint TemplateId { get; init; }
    public uint GroupType { get; init; }
    public uint GroupId { get; init; }
    public byte GroupMemberIdx { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float ZRot { get; init; }
    public float Scale { get; init; }
}

public static class ZwSpawnNpcParser
{
    public static ZwSpawnNpcParsed? TryParse(byte[] raw)
    {
        // Live ZWSpawnNpc from dedicate is a fixed 78-byte body. Reject other blobs
        // (e.g. player WZUnitState stored in the same UnitRegistry).
        if (raw == null || raw.Length != 78)
            return null;

        try
        {
            var s = new PacketStream();
            s.Insert(0, raw);

            var sid = s.ReadUInt32();
            var sType = s.ReadUInt32();
            var mIdx = s.ReadByte();
            var pIdx = s.ReadByte();
            var tIdx = s.ReadUInt16();
            var templateId = s.ReadUInt32();

            var groupType = s.ReadUInt32();
            var groupId = s.ReadUInt32();
            var groupMemberIdx = s.ReadByte();

            // pos is vec3 f32 (ISerialize vt+208), not 11-byte quantized worldPos
            var x = s.ReadSingle();
            var y = s.ReadSingle();
            var z = s.ReadSingle();
            var zRot = s.ReadSingle();
            var scale = s.ReadSingle();

            if (templateId == 0)
                return null;

            return new ZwSpawnNpcParsed
            {
                SpawnerId = sid,
                SpawnerType = sType,
                MemberIdx = mIdx,
                PartIdx = pIdx,
                TableIdx = tIdx,
                TemplateId = templateId,
                GroupType = groupType,
                GroupId = groupId,
                GroupMemberIdx = groupMemberIdx,
                X = x,
                Y = y,
                Z = z,
                ZRot = zRot,
                Scale = scale <= 0f ? 1f : scale
            };
        }
        catch
        {
            return null;
        }
    }
}

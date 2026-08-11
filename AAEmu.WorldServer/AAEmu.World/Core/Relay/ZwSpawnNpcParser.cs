using AAEmu.Commons.Network;
using AAEmu.Game.GameData;

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
    /// <summary>Minimum body for fixed header+pos (sid…scale). Ambient pad is 78 B.</summary>
    public const int MinBodyLength = 41;

    /// <summary>Retail ambient body size (zeroed optional tail).</summary>
    public const int AmbientBodyLength = 78;

    public static ZwSpawnNpcParsed? TryParse(byte[] raw)
    {
        // Ambient is 78 B; event OnEvent may append creator fields. Soft-cap avoids UnitState dumps.
        if (raw == null || raw.Length < MinBodyLength || raw.Length > 512)
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

            // Type-2 group path sometimes writes template 0; resolve first Npc member from sType.
            if (templateId == 0 && sType != 0)
                templateId = ResolveTemplateFromSpawnerType(sType);

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

    /// <summary>Peek sid/sType for TRACE when full parse fails.</summary>
    public static bool TryPeekIds(byte[] raw, out uint spawnerId, out uint spawnerType)
    {
        spawnerId = 0;
        spawnerType = 0;
        if (raw == null || raw.Length < 8)
            return false;
        spawnerId = BitConverter.ToUInt32(raw, 0);
        spawnerType = BitConverter.ToUInt32(raw, 4);
        return true;
    }

    private static uint ResolveTemplateFromSpawnerType(uint spawnerType)
    {
        try
        {
            var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerType);
            if (template?.Npcs == null)
                return 0;

            foreach (var member in template.Npcs)
            {
                if (member == null || member.MemberId == 0)
                    continue;
                if (string.Equals(member.MemberType, "Npc", StringComparison.OrdinalIgnoreCase))
                    return member.MemberId;
            }

            // NpcGroup wire with templateId 0 — leave reject for non-Npc membership.
            // Member Npc templates may still sit inside group tables only if desc expands them
            // differently; World will still TRACE reject for pure groups with zero template.
        }
        catch
        {
            // GameData not ready — reject as before.
        }

        return 0;
    }
}

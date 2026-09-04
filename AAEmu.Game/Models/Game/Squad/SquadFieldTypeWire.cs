using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Squad;

/// <summary>
/// Which instance a squad is for. Three fields, not one: the kind selects the id space of
/// <see cref="Value"/>, while <see cref="InstanceId"/> stays in the instance-catalog space and
/// is what the client's matchmaking check resolves before it will send a matching request.
/// </summary>
/// <param name="Kind">Selects the table behind <paramref name="Value"/>.</param>
/// <param name="InstanceId">Instance catalog id (<c>instances.id</c>).</param>
/// <param name="Value">Id in the space named by <paramref name="Kind"/>.</param>
public readonly record struct SquadFieldType(byte Kind, uint InstanceId, ulong Value)
{
    /// <summary><see cref="Value"/> is an <c>instances.id</c>.</summary>
    public const byte CatalogKind = 1;

    /// <summary><see cref="Value"/> is a <c>zone_group_id</c>, read by the client as a u16.</summary>
    public const byte ZoneGroupKind = 2;
}

/// <summary>
/// Wire form of <see cref="SquadFieldType"/>. On the wire the kind is one byte; in the client's
/// SquadBase it occupies a full u32, so the id that follows it lands at a fixed offset the
/// matchmaking path reads directly. Collapsing the two into a single u64 leaves that id zero,
/// and the client then refuses to start matching at all.
/// </summary>
public static class SquadFieldTypeWire
{
    /// <summary>CS packets: u8 kind, u32 instanceId, u64 value.</summary>
    public static SquadFieldType Read(PacketStream stream)
    {
        var kind = stream.ReadByte();
        var instanceId = stream.ReadUInt32();
        var value = stream.ReadUInt64();
        return new SquadFieldType(kind, instanceId, value);
    }

    /// <summary>Inline SC form (e.g. SCInviteSquadMember), same layout as the CS form.</summary>
    public static void WriteInline(PacketStream stream, SquadFieldType field)
    {
        stream.Write(field.Kind);
        stream.Write(field.InstanceId);
        stream.Write(field.Value);
    }

    /// <summary>SquadBase blob: u32 kind, u32 instanceId, u64 value.</summary>
    public static void WriteSquadBaseBlob(PacketStream stream, SquadFieldType field)
    {
        stream.Write((uint)field.Kind);
        stream.Write(field.InstanceId);
        stream.Write(field.Value);
    }
}

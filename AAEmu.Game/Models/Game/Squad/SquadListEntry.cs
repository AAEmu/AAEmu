using System.Text;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Squad;

/// <summary>
/// One recruit-board / create-ack row. Wire layout locked from client
/// <c>X2::SquadBase</c> reader <c>sub_39BD3680</c> (mask 0x0F).
/// </summary>
public class SquadListEntry : PacketMarshaler
{
    public const byte WireMask = 0x0F;

    public uint SquadId { get; set; }
    public SquadOpenType OpenType { get; set; }
    public string OwnerName { get; set; } = "";
    public byte OwnerLevel { get; set; }
    public string WorldName { get; set; } = "";
    public string ExplanationText { get; set; } = "";
    public byte LimitLevel { get; set; }
    public int LimitGearScore { get; set; }
    /// <summary>
    /// Which instance this squad is for. The kind and value drive the client's title and member
    /// cap; the instance id is what its matchmaking check resolves before it will register.
    /// </summary>
    public SquadFieldType Field { get; set; }

    public bool IsMySquad { get; set; }
    public bool ButtonEnable { get; set; }
    public byte ButtonType { get; set; }

    /// <summary>Leader world char key (mask bit 4 lookup + blob +112).</summary>
    public ulong LeaderWorldCharKey { get; set; }

    /// <summary>
    /// Non-zero once the squad is queued for matching. The client treats this as "matching
    /// applied" and, together with <see cref="IsJoining"/>, it gates the entry confirmation.
    /// </summary>
    public ulong MatchingKey { get; set; }

    /// <summary>Set while the squad is joining a match; required by the entry confirmation.</summary>
    public bool IsJoining { get; set; }

    /// <summary>
    /// Whether the squad's game has already begun. The client refuses to enter an instance while
    /// this is set, so it must stay clear until the squad is actually inside.
    /// </summary>
    public bool IsStarted { get; set; }

    /// <summary>World hosting the squad's game.</summary>
    public byte GameWorldId { get; set; }

    /// <summary>Public join key.</summary>
    public uint PublicKey { get; set; }

    /// <summary>Catalog id duplicate on wire (+104); defaults to <see cref="FieldType"/>.</summary>
    public uint CatalogWireId { get; set; }

    /// <summary>Leading i32 of the 8-byte block at +28 (client default 255).</summary>
    public int HeaderField { get; set; } = 255;

    /// <summary>Trailing u8 of the 8-byte block at +28 (+32); often world id.</summary>
    public byte HeaderByte { get; set; }

    /// <summary>World id folded into every member key so the client can name their server.</summary>
    public byte WorldId { get; set; }

    /// <summary>
    /// Members for mask bit 8. These build the client's member map, which is also what the
    /// leader key after them is resolved against — an empty array leaves the client with no
    /// leader, so it offers neither the leader's buttons nor any member's details.
    /// </summary>
    public IReadOnlyList<SquadMember> Members { get; set; } = [];

    public override PacketStream Write(PacketStream stream)
    {
        if ((WireMask & 1) != 0)
            WriteMask1(stream);

        if ((WireMask & 2) != 0)
            stream.Write((int)OpenType);

        if ((WireMask & 8) != 0)
            WriteMask8(stream);

        if ((WireMask & 4) != 0)
            stream.Write(LeaderWorldCharKey);

        return stream;
    }

    private void WriteMask1(PacketStream stream)
    {
        stream.Write(SquadId);
        WriteHeaderBlock(stream);
        WriteFieldTypeBlob(stream);
        stream.Write(CatalogWireId != 0 ? CatalogWireId : Field.InstanceId);
        stream.Write(LeaderWorldCharKey);
        WriteLenString(stream, ExplanationText);
        WriteMatchingBlock(stream);
        stream.Write(IsStarted ? (byte)1 : (byte)0);
        stream.Write(GameWorldId);
        stream.Write(PublicKey);
        WriteLimitsAndFieldTail(stream);
    }

    private void WriteHeaderBlock(PacketStream stream)
    {
        stream.Write(HeaderField);
        stream.Write(HeaderByte);
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write((byte)0);
    }

    private void WriteFieldTypeBlob(PacketStream stream)
    {
        SquadFieldTypeWire.WriteSquadBaseBlob(stream, Field);
    }

    private void WriteMatchingBlock(PacketStream stream)
    {
        stream.Write(MatchingKey);
        stream.Write(IsJoining ? (byte)1 : (byte)0);
        WriteZeroBlob(stream, 7);
    }

    private void WriteLimitsAndFieldTail(PacketStream stream)
    {
        stream.Write(LimitLevel);
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write(LimitGearScore);
    }

    private void WriteMask8(PacketStream stream)
    {
        // Only emit as many member blobs as we actually write. Advertising CurMemberCount
        // without bodies leaves the reader short on mask-4 (leader u64) and throws.
        var members = Members;
        var count = members == null ? 0 : Math.Min(byte.MaxValue, members.Count);
        stream.Write((byte)count);
        for (var i = 0; i < count; i++)
            SquadMemberWire.WriteEmbeddedMask8(stream, members![i], WorldId);
    }

    private static void WriteZeroBlob(PacketStream stream, int size)
    {
        stream.Write(new byte[size], appendSize: false);
    }

    private static void WriteLenString(PacketStream stream, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value ?? "");
        if (encoded.Length > ushort.MaxValue)
            encoded = encoded.AsSpan(0, ushort.MaxValue).ToArray();
        stream.Write((ushort)encoded.Length);
        if (encoded.Length > 0)
            stream.Write(encoded, appendSize: false);
    }
}

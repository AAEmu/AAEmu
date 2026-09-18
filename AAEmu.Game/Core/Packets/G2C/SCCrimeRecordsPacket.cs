using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One crime row of a character's record: what the trial window lists as reporter, charge, victim
/// and memo for a single offence.
/// </summary>
/// <remarks>
/// Field order matches the client's row serializer, whose stream slots the widths come from:
/// id (u32), type (u64), victim name, type (u64), reporter name, two unnamed u32 fields,
/// crimeKind (u8), two unnamed u32 fields, x and y (eight bytes each), z (four bytes),
/// description, time (u64). The unnamed slots carry no meaning in the client's own reader, so they
/// are passed through as <see cref="Field1"/>..<see cref="Field4"/>.
/// </remarks>
public readonly record struct CrimeRecordEntry(
    uint Id,
    ulong Type,
    string VictimName,
    ulong Type2,
    string ReporterName,
    uint Field1,
    uint Field2,
    byte CrimeKind,
    uint Field3,
    uint Field4,
    double X,
    double Y,
    float Z,
    string Description,
    ulong Time);

/// <summary>
/// Crime record counters of one character: the trial they are in, the trial type, how many records
/// exist in total and the rows carried by this packet.
/// </summary>
/// <remarks>
/// Wire layout the 10.0.2.13 client's reader expects: "trialId" (u64), "trialType" (u32),
/// "total" (u32), "count" (u32); when count &gt; 0 the packet continues with that many row records in
/// the order above.
/// </remarks>
public class SCCrimeRecordsPacket : GamePacket
{
    private readonly ulong _trialId;
    private readonly uint _trialType;
    private readonly uint _total;
    private readonly uint _count;
    private readonly IReadOnlyList<CrimeRecordEntry> _records;

    /// <summary>Counters only, for the record totals a character carries outside a trial.</summary>
    public SCCrimeRecordsPacket(ulong trialId, uint trialType, uint total, uint count)
        : this(trialId, trialType, total, count, [])
    {
    }

    /// <summary>The record rows themselves; the wire count follows the list.</summary>
    public SCCrimeRecordsPacket(ulong trialId, uint trialType, uint total, IReadOnlyList<CrimeRecordEntry> records)
        : this(trialId, trialType, total, (uint)records.Count, records)
    {
    }

    private SCCrimeRecordsPacket(ulong trialId, uint trialType, uint total, uint count,
        IReadOnlyList<CrimeRecordEntry> records)
        : base(SCOffsets.SCCrimeRecordsPacket, 1)
    {
        _trialId = trialId;
        _trialType = trialType;
        _total = total;
        _count = count;
        _records = records;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_trialId);
        stream.Write(_trialType);
        stream.Write(_total);
        stream.Write(_count);

        foreach (var record in _records)
        {
            stream.Write(record.Id);
            stream.Write(record.Type);
            stream.Write(record.VictimName);
            stream.Write(record.Type2);
            stream.Write(record.ReporterName);
            stream.Write(record.Field1);
            stream.Write(record.Field2);
            stream.Write(record.CrimeKind);
            stream.Write(record.Field3);
            stream.Write(record.Field4);
            stream.Write(record.X);
            stream.Write(record.Y);
            stream.Write(record.Z);
            stream.Write(record.Description);
            stream.Write(record.Time);
        }

        return stream;
    }
}

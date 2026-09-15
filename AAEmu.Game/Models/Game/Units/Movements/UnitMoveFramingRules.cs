namespace AAEmu.Game.Models.Game.Units.Movements;

/// <summary>
/// Whether a movement record can be walked to its end. A batch is a run of records with no per-record
/// length, so a record whose tail this build cannot read leaves the reader pointing into the middle of
/// the next one: everything after it parses as noise. Recognition is what lets a caller stop there
/// instead of relaying positions that were never on the wire.
/// </summary>
public static class UnitMoveFramingRules
{
    /// <summary>
    /// The actor flag whose tail is unparsed. It gates the client's "pushed by another unit" blob,
    /// whose length is not known from any capture we have, so the record cannot be framed.
    /// </summary>
    public const ushort UnreadableTailFlag = 0x8000;

    /// <summary>True for a record whose tail this build cannot walk past.</summary>
    public static bool HasUnreadableTail(ushort actorFlags) =>
        (actorFlags & UnreadableTailFlag) != 0;
}

namespace AAEmu.Game.Models.Game.Residents;

/// <summary>
/// One <c>character_resident_state</c> row: what one character has contributed to (and paid into)
/// one zone group's resident balance. Written at every settlement, read by the townhall packets.
/// </summary>
public sealed record CharacterResidentState(
    uint OwnerId,
    ushort ZoneGroupId,
    uint ServicePoint,
    ulong Charge,
    ulong HuntingCharge,
    DateTime UpdatedAt);

/// <summary>Outcome of one resident settlement attempt.</summary>
public enum ResidentSettleStatus
{
    /// <summary>Nothing was written: the request itself was invalid or carried unresolved fields.</summary>
    Refused,

    /// <summary>The row was written.</summary>
    Settled,
}

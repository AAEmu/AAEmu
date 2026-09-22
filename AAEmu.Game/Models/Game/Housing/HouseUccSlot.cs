namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// One of the five user-created-content slots a house carries. The client reads all five from the
/// house payload on every load, so an occupied slot replays without any extra packet.
/// </summary>
public sealed class HouseUccSlot
{
    /// <summary>Applied UCC id, zero while the slot is empty.</summary>
    public ulong UccId { get; set; }

    /// <summary>Slot kind the sender chose when the UCC was applied.</summary>
    public uint Kind { get; set; }

    /// <summary>Placement position the sender chose when the UCC was applied.</summary>
    public uint Position { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool Occupied => UccId != 0;

    public void Clear()
    {
        UccId = 0;
        Kind = 0;
        Position = 0;
    }
}

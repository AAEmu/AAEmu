namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// Persisted cross-server transfer state, stored in <c>character_transfer_journals.state</c>.
/// The values are the TINYINT encoding of the journal row (protocol/status bytes), not tuning:
/// gameplay numbers for this feature do not exist in content, so none are invented here.
/// </summary>
public enum CrossServerTransferState : byte
{
    /// <summary>Departure journaled: the character is parked and its state snapshot taken.</summary>
    Parked = 1,

    /// <summary>The outbound transfer completed on this server and settled (still awaiting re-entry).</summary>
    Transferred = 2,

    /// <summary>Failure (or restart recovery) rolled the departure back; the character was restored.</summary>
    RolledBack = 3,

    /// <summary>Re-entry accepted the settled transfer and consumed the journal.</summary>
    Reentered = 4,
}

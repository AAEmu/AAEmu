namespace AAEmu.Game.Models.Game.PlotAuctions;

/// <summary>
/// The persisted state machine of one plot auction (keyed by <c>plot_auction_config.id</c>).
/// A row is created with the first bid and lives until settlement, so a restart reloads exactly
/// the state the process held.
/// </summary>
public sealed class PlotAuction
{
    /// <summary>Config id (also the store primary key).</summary>
    public uint Id { get; init; }

    /// <summary>Activity the auction belongs to, copied from the config at creation.</summary>
    public uint ActivityId { get; init; }

    /// <summary>Current price base: the standing leading bid, or 0 before the first bid (the
    /// client then falls back to the config's start price). Persisted so the floor a restart
    /// enforces is the floor the live server enforced.</summary>
    public long BasePrice { get; set; }

    /// <summary>True once settlement has consumed every escrow row. A settled auction refuses a
    /// second settlement attempt.</summary>
    public bool Settled { get; set; }
}

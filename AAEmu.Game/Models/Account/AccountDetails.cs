namespace AAEmu.Game.Models.Account;

public struct AccountDetails
{
    public int AccountId { get; set; }
    public int AccessLevel { get; set; }
    public short Labor { get; set; }

    /// <summary>
    /// The SERVER-LOCAL labor pool, which the client labels "Online Labor". Account-scoped like
    /// <see cref="Labor"/>: the client keeps a single labor manager per session, so a per-character
    /// balance can never be shown correctly in the character-select header.
    /// </summary>
    public int LocalLabor { get; set; }
    public int Credits { get; set; }
    public int Loyalty { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime LastLogin { get; set; }
    public DateTime LastLaborTick { get; set; }
    public DateTime LastCreditsTick { get; set; }
    public DateTime LastLoyaltyTick { get; set; }
}

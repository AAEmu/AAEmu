using System;

namespace AAEmu.Game.Models.Account;

public struct AccountDetails
{
    public ulong AccountId { get; set; }
    public int AccessLevel { get; set; }
    public short Labor { get; set; }
    public int Credits { get; set; }
    public int Loyalty { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime LastLogin { get; set; }
    public DateTime LastLaborTick { get; set; }
    public DateTime LastCreditsTick { get; set; }
    public DateTime LastLoyaltyTick { get; set; }
}

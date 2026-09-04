using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Models;

#pragma warning disable IDE0052 // Remove unread private members

public class AccountPayment(GameConnection connection)
{
    private GameConnection _connection = connection;

    public PaymentMethodType Method { get; set; } = PaymentMethodType.Premium;
    public int Location { get; set; } = 1;

    /// <summary>
    /// Start of the paid period. A fixed past date rather than DateTime.MinValue, which serializes to a
    /// unix time of 0 and told the client the subscription began in 1970.
    /// </summary>
    public DateTime StartTime { get; set; } = new(2020, 1, 1);
    public DateTime EndTime { get; set; } = new(2030, 1, 1);

    /// <summary>
    /// Paid time left, in seconds. The client reads realPayTime through its plain int64 slot rather
    /// than its DateTime slot (client serializer), so this is a duration, not a timestamp -
    /// and it was previously hardcoded to 0 on the wire.
    /// </summary>
    public long RealPayTimeSeconds
    {
        get
        {
            var remaining = EndTime - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? 0L : (long)remaining.TotalSeconds;
        }
    }

    /// <summary>How many times premium was bought. No purchase records exist, so report one.</summary>
    public int BuyPremiumCount { get; set; } = 1;

    /// <summary>
    /// Checks if Premium is currently active
    /// </summary>
    public bool PremiumState
    {
        get => Method == PaymentMethodType.Premium && DateTime.UtcNow >= StartTime && DateTime.UtcNow <= EndTime;
    }
}

/// <summary>
/// Registered payment type.
/// Scripts seem to reference the following types related to labor info: person, person_time, pcbang, trial, event (siege_event)
/// </summary>
public enum PaymentMethodType
{
    Premium = 1,
    Demo = 3,
    None = 5
}

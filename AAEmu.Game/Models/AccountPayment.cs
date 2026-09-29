namespace AAEmu.Game.Models;

/// <summary>
/// One row of <c>account_payments</c>: the account's payment tier, exactly as the 10.x connection
/// path publishes it in <c>SCAccountInfo</c> and as the paid entitlements branch on it.
/// </summary>
/// <remarks>
/// Nothing here is a shipped default. Every field is read from the account's row by
/// <see cref="Core.Managers.AccountPaymentManager"/>; an account with no row is left on the
/// no-entitlement state, never on a paid one.
/// </remarks>
public sealed record AccountPaymentRecord
{
    public uint AccountId { get; init; }
    public PaymentMethodType Method { get; init; }

    /// <summary>Where the subscription was bought. 0 is the store this server does not operate.</summary>
    public int Location { get; init; }

    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }

    /// <summary>How many times premium was bought on this account.</summary>
    public int BuyPremiumCount { get; init; }

    /// <summary>
    /// Whether this row is still the untouched placeholder a migration or a fresh account INSERT
    /// wrote: the no-entitlement tier, no subscription window, and no recorded purchase.
    /// </summary>
    /// <remarks>
    /// The placeholder is a marker, not a decision. A SQL migration cannot read the server's
    /// configuration, so it cannot seed the tier an operator asked for, and a placeholder written as
    /// the free tier is exactly the "every existing account drops to free" fault this row exists to
    /// prevent. Recognising the placeholder lets the loader replace it once, from configuration, so
    /// the duration lives in one place instead of being duplicated into a migration.
    /// <para>
    /// Every field is checked. A row that merely has no window is an expired subscription, which is a
    /// real state a real purchase can leave behind, and must not be silently re-granted.
    /// </para>
    /// </remarks>
    public bool IsUntouchedSeededDefault =>
        Method == PaymentMethodType.None &&
        Location == 0 &&
        BuyPremiumCount == 0 &&
        ServerCalendar.AsUtc(StartTime) == AccountPayment.NoSubscriptionTime &&
        ServerCalendar.AsUtc(EndTime) == AccountPayment.NoSubscriptionTime;
}

/// <summary>
/// Account payment state for one connection. Every field is assigned from the account's
/// <c>account_payments</c> row; until that row is loaded this reports no paid entitlement, so a
/// missing or unreadable row can never be mistaken for a paid one.
/// </summary>
public class AccountPayment
{
    /// <summary>
    /// The wire form for "this account holds no subscription window". A DateTime.MinValue would
    /// serialize to a unix time of zero anyway; naming the epoch keeps the intent readable.
    /// </summary>
    public static readonly DateTime NoSubscriptionTime = DateTime.UnixEpoch;

    /// <summary>
    /// The wire form for a window that does not close. This is a sentinel rather than a date content
    /// wrote: the seeded tier is granted, not bought, so it has no subscription to run out of, and
    /// inventing a year for it would both be a shipped value in C# and quietly expire on its own.
    /// </summary>
    /// <remarks>
    /// It is deliberately the far end of the MySQL <c>DATETIME</c> range, because that column is what
    /// the window is stored in. Note this is <b>not</b> the same as
    /// <see cref="NoSubscriptionTime"/>: that is a window that closed at the epoch, which reads as
    /// expired, and confusing the two is how a row came to say <c>Premium</c> and mean it.
    /// </remarks>
    public static readonly DateTime NoExpiryTime =
        new(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);

    public PaymentMethodType Method { get; private set; } = PaymentMethodType.None;
    public int Location { get; private set; }

    public DateTime StartTime { get; private set; } = NoSubscriptionTime;
    public DateTime EndTime { get; private set; } = NoSubscriptionTime;

    public int BuyPremiumCount { get; private set; }

    /// <summary>
    /// True once an <c>account_payments</c> row has been applied. Entitlements consult this through
    /// <see cref="PremiumState"/>, so the tier is only ever honoured when it was actually loaded.
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Applies the account's persisted row. Called by the connection path, once per session.</summary>
    public void Apply(AccountPaymentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Method = record.Method;
        Location = record.Location;
        StartTime = ServerCalendar.AsUtc(record.StartTime);
        EndTime = ServerCalendar.AsUtc(record.EndTime);
        BuyPremiumCount = record.BuyPremiumCount;
        IsLoaded = true;
    }

    /// <summary>
    /// Drops back to the no-entitlement state. Used when the row is missing or unreadable, so a
    /// failed load leaves a deterministic free tier rather than whatever was there before.
    /// </summary>
    public void Clear()
    {
        Method = PaymentMethodType.None;
        Location = 0;
        StartTime = NoSubscriptionTime;
        EndTime = NoSubscriptionTime;
        BuyPremiumCount = 0;
        IsLoaded = false;
    }

    /// <summary>
    /// Paid time left, in seconds. The client reads realPayTime through its plain int64 slot rather
    /// than its DateTime slot, so this is a duration, not a timestamp.
    /// </summary>
    public long RealPayTimeSeconds
    {
        get
        {
            if (!IsLoaded)
                return 0L;
            var remaining = EndTime - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? 0L : (long)remaining.TotalSeconds;
        }
    }

    /// <summary>
    /// Whether this account currently holds an active paid subscription. A payment that was never
    /// loaded is not premium, which is what keeps a missing row from granting paid entitlements.
    /// </summary>
    public bool PremiumState
    {
        get => IsLoaded &&
               Method == PaymentMethodType.Premium &&
               DateTime.UtcNow >= StartTime &&
               DateTime.UtcNow <= EndTime;
    }

    /// <summary>One line naming the tier and the remaining window, for the session log.</summary>
    public string Describe() =>
        IsLoaded
            ? $"method={Method} location={Location} start={StartTime:O} end={EndTime:O} " +
              $"remainingSeconds={RealPayTimeSeconds} buyCount={BuyPremiumCount} premium={PremiumState}"
            : "not loaded (no account_payments row) premium=False";
}

/// <summary>
/// Registered payment type. These are wire values read by the client, not gameplay content.
/// Scripts seem to reference the following types related to labor info: person, person_time, pcbang, trial, event (siege_event)
/// </summary>
public enum PaymentMethodType
{
    Premium = 1,
    Demo = 3,
    None = 5
}

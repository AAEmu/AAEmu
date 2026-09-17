namespace AAEmu.Game.Models.Game.SensitiveOperation;

/// <summary>The actions an account's protection window covers.</summary>
/// <remarks>
/// The three the client's own window names for this guard: destroying an item, confirming a trade, and
/// sending mail. They are the irreversible ones — everything else the protection could cover is undoable.
/// </remarks>
public enum SensitiveOperationKind
{
    ItemDestruction,
    Trade,
    Mail
}

/// <summary>What the client is told about an account's protection window.</summary>
public readonly record struct SensitiveOperationState(bool Protected, uint RemainSeconds);

/// <summary>
/// The protection window's decisions, kept apart from the manager so they can be exercised without an
/// account, a feature set or a client.
/// </summary>
public static class SensitiveOperationRules
{
    /// <summary>
    /// How long a window lasts before it lapses on its own.
    /// </summary>
    /// <remarks>
    /// A local choice, not a retail value: the client renders a countdown from the field the guard answers
    /// with, and the length this stack's source material uses is not readable in any table or capture we
    /// have. Ten minutes is long enough to cover the risky part of a session and short enough that a window
    /// nobody verifies stops being in the way. It is one constant, so a measured value can replace it.
    /// </remarks>
    public static readonly TimeSpan DefaultProtectionWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether a sensitive action may go ahead: only a live window on an account that has not verified stops
    /// it, and the whole guard is inert while its feature bit is off — which is how it ships. The client only
    /// draws the account-protection indicator and asks for the state once bit 56 is set.
    /// </summary>
    public static bool MayPerform(bool featureEnabled, bool protectedNow, bool verifiedNow) =>
        !featureEnabled || !protectedNow || verifiedNow;

    /// <summary>Seconds left in a window, zero once it has passed.</summary>
    public static uint RemainingSeconds(DateTime expiresAtUtc, DateTime nowUtc)
    {
        var left = expiresAtUtc - nowUtc;
        return left <= TimeSpan.Zero ? 0u : (uint)Math.Ceiling(left.TotalSeconds);
    }

    /// <summary>Whether a window has run out.</summary>
    public static bool IsExpired(DateTime expiresAtUtc, DateTime nowUtc) => nowUtc >= expiresAtUtc;

    /// <summary>
    /// Whether a cancel names the verification that is actually pending. A cancel for anything else is
    /// ignored rather than clearing a verification the player is still looking at.
    /// </summary>
    public static bool CancelsPendingVerification(bool hasPending, int pendingSequence, int cancelledSequence) =>
        hasPending && pendingSequence == cancelledSequence;

    /// <summary>
    /// The next verification sequence number. The client hands the number back when it cancels, so it must
    /// change every time a verification starts; zero is skipped because the client treats it as "none".
    /// </summary>
    public static int NextSequence(int current)
    {
        var next = current + 1;
        return next <= 0 ? 1 : next;
    }
}

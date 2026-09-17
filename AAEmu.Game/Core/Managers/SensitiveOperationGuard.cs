using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.SensitiveOperation;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The account-protection window over the irreversible actions — destroying an item, confirming a trade,
/// sending mail.
/// </summary>
/// <remarks>
/// <para>
/// The whole guard is governed by feature bit 56 (<see cref="Feature.sensitiveOpeartion"/>), which ships
/// <c>false</c> here on purpose: with the bit off the client never draws its account-protection indicator and
/// never asks for the state, so none of this code changes behaviour. Turn the bit on and the guard becomes
/// live — the client asks, the answer carries the countdown, and the three actions are held back until the
/// account verifies.
/// </para>
/// <para>
/// Verification is the in-game second password, not a web page. The client's verification dialog opens a URL
/// the server supplies (<c>SCSensitiveOperationVerifyUrlPacket</c> 0x291), which in retail is the publisher's
/// account page — this stack has no such page, so that packet is deliberately never sent and the second
/// password (feature bit 57) is the verifier instead. An account with no second password is refused a window
/// rather than being locked out of its own items.
/// </para>
/// <para>
/// State is per account and in memory: a window is a session-scale thing, and the World restarting clearing
/// it is the same outcome as the countdown running out.
/// </para>
/// </remarks>
public static class SensitiveOperationGuard
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private sealed class Window
    {
        public DateTime ExpiresAtUtc { get; set; }
        public int PendingSequence { get; set; }
        public bool HasPendingVerification { get; set; }
    }

    private static readonly Dictionary<uint, Window> _windows = [];
    private static readonly object _lock = new();
    private static int _lastSequence;

    /// <summary>Whether the guard is switched on for this stack (feature bit 56).</summary>
    public static bool IsEnabled => FeaturesManager.Fsets?.Check(Feature.sensitiveOpeartion) == true;

    /// <summary>The account's window, with an expired one reported as no window at all.</summary>
    public static SensitiveOperationState StateFor(uint accountId, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue(accountId, out var window))
                return new SensitiveOperationState(false, 0);

            if (SensitiveOperationRules.IsExpired(window.ExpiresAtUtc, nowUtc))
            {
                _windows.Remove(accountId);
                return new SensitiveOperationState(false, 0);
            }

            return new SensitiveOperationState(true,
                SensitiveOperationRules.RemainingSeconds(window.ExpiresAtUtc, nowUtc));
        }
    }

    /// <summary>
    /// Answers the client's state request (CS 0x19A) with the defined result packet, so the indicator can
    /// draw the countdown. Nothing is sent while the guard is off.
    /// </summary>
    public static void SendState(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        if (character == null || !IsEnabled)
            return;

        var state = StateFor(connection.AccountId, DateTime.UtcNow);
        character.SendPacket(new SCProtectSensitiveOperationResultPacket(
            (byte)(state.Protected ? 1 : 0), state.RemainSeconds));
    }

    /// <summary>
    /// Opens or closes a window for the account. Refused when the guard is off, and refused when the account
    /// has no second password to verify with — a window nobody can lift would lock the player out of their own
    /// items, so it is never opened in that state.
    /// </summary>
    public static bool TrySetProtection(Character character, bool protect, out string refusal)
    {
        refusal = null;
        if (character?.Connection == null)
            return false;

        if (!IsEnabled)
        {
            refusal = "Sensitive-operation protection is switched off on this server.";
            return false;
        }

        var accountId = character.Connection.AccountId;
        if (protect && !SecondPasswordManager.Instance.HasPassword(accountId))
        {
            refusal = "Set a second password first - it is what a protected account verifies with.";
            return false;
        }

        lock (_lock)
        {
            if (!protect)
            {
                _windows.Remove(accountId);
            }
            else
            {
                _windows[accountId] = new Window
                {
                    ExpiresAtUtc = DateTime.UtcNow.Add(SensitiveOperationRules.DefaultProtectionWindow)
                };
            }
        }

        Logger.Info("Account {0} ({1}) {2} sensitive-operation protection", accountId, character.Name,
            protect ? "entered" : "left");
        SendState(character.Connection);
        return true;
    }

    /// <summary>
    /// Whether the character may carry out <paramref name="kind"/> right now. A refusal starts (or reuses) a
    /// verification for the account and says so in chat, because the client has no dialog of its own for this
    /// and the second password's window is where the player can act on it.
    /// </summary>
    public static bool MayPerform(Character character, SensitiveOperationKind kind, out string reason)
    {
        reason = null;
        if (character?.Connection == null || !IsEnabled)
            return true;

        var accountId = character.Connection.AccountId;
        var state = StateFor(accountId, DateTime.UtcNow);
        if (SensitiveOperationRules.MayPerform(true, state.Protected, false))
            return true;

        lock (_lock)
        {
            var sequence = SensitiveOperationRules.NextSequence(_lastSequence);
            _lastSequence = sequence;
            _windows[accountId] = new Window
            {
                ExpiresAtUtc = DateTime.UtcNow.Add(SensitiveOperationRules.DefaultProtectionWindow),
                PendingSequence = sequence,
                HasPendingVerification = true
            };
        }

        Logger.Info("{0} tried {1} while account {2} is protected", character.Name, kind, accountId);
        reason = "Your account is under protection: verify with your second password before this action.";
        return false;
    }

    /// <summary>
    /// The client cancelled the verification it was shown (CS 0x19B). Only the sequence that is actually
    /// pending clears it.
    /// </summary>
    public static void CancelVerification(Character character, int sequence)
    {
        if (character?.Connection == null)
            return;

        var accountId = character.Connection.AccountId;
        lock (_lock)
        {
            if (!_windows.TryGetValue(accountId, out var window))
                return;

            if (!SensitiveOperationRules.CancelsPendingVerification(window.HasPendingVerification,
                    window.PendingSequence, sequence))
                return;

            window.HasPendingVerification = false;
        }

        Logger.Debug("Account {0} cancelled sensitive-operation verification {1}", accountId, sequence);
    }

    /// <summary>
    /// A second password was accepted. That is this guard's verification, so the window is lifted and the
    /// client is told (SC 0x295), which is what draws its "security mode lifted" notice.
    /// </summary>
    public static void OnSecondPasswordVerified(Character character)
    {
        if (character?.Connection == null || !IsEnabled)
            return;

        var accountId = character.Connection.AccountId;
        lock (_lock)
        {
            if (!_windows.Remove(accountId))
                return;
        }

        character.SendPacket(new SCSensitiveOperationVerifySuccessPacket());
        Logger.Info("Account {0} ({1}) verified; sensitive-operation protection lifted", accountId,
            character.Name);
    }

    /// <summary>Forgets an account's window (a character leaving the world).</summary>
    public static void Clear(uint accountId)
    {
        lock (_lock)
            _windows.Remove(accountId);
    }

    /// <summary>The state a GM surface reports: the window and whether the guard is on at all.</summary>
    public static string Describe(Character character)
    {
        if (character?.Connection == null)
            return "no character";

        var state = StateFor(character.Connection.AccountId, DateTime.UtcNow);
        var enabled = IsEnabled ? "on" : "off (feature bit 56)";
        return state.Protected
            ? $"guard {enabled}; this account is protected for another {state.RemainSeconds}s"
            : $"guard {enabled}; this account is not protected";
    }
}

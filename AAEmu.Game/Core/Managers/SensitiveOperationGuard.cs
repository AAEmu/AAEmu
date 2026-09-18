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
/// Verification is the in-game second password (<see cref="Feature.secondpass"/>, bit 46), not a web page. The
/// client's verification dialog opens a URL the server supplies (<c>SCSensitiveOperationVerifyUrlPacket</c>
/// 0x291), which in retail is the publisher's account page — this stack has no such page, so that packet is
/// deliberately never sent and <see cref="OnSecondPasswordVerified"/> is what lifts a window. Nothing the
/// client sends moves a window: its account-protection request (CS 0x19A) is a state query, so the
/// <c>/sensitive</c> command is the only way a window is opened outside verification. An account with no
/// second password is refused a window rather than being locked out of its own items.
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
    }

    private static readonly Dictionary<uint, Window> _windows = [];
    private static readonly object _lock = new();

    /// <summary>
    /// The clock the windows run on. Production is the system clock; the test assembly swaps in a fake one so
    /// a countdown can be walked without sleeping.
    /// </summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    private static DateTime UtcNow => Clock.GetUtcNow().UtcDateTime;

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

        var state = StateFor(connection.AccountId, UtcNow);

        // The field is milliseconds, not seconds. The client keeps the countdown itself: its account-protection
        // scripts (x2ui/hud/indicators/account_protection.lua, x2ui/sensitiveoperation/remaintime.lua) seed a
        // timer with this value, subtract the frame delta from it and print it as value / 1000. Sending seconds
        // would render a ten-minute window as 0.6 seconds; nothing about the layout changes with the unit.
        character.SendPacket(new SCProtectSensitiveOperationResultPacket(
            (byte)(state.Protected ? 1 : 0), state.RemainSeconds * 1000u));
    }

    /// <summary>
    /// Opens or closes a window for the account. Refused when the guard is off, and refused when the account
    /// has no second password to verify with — a window nobody can lift would lock the player out of their own
    /// items, so it is never opened in that state.
    /// </summary>
    /// <remarks>
    /// The client cannot ask for either: its account-protection packet is a state query, and only a verified
    /// second password (<see cref="OnSecondPasswordVerified"/>) or this method — the GM surface — moves a
    /// window.
    /// </remarks>
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
                    ExpiresAtUtc = UtcNow.Add(SensitiveOperationRules.DefaultProtectionWindow)
                };
            }
        }

        Logger.Info("{0} {1} sensitive-operation protection", character.Name, protect ? "entered" : "left");
        SendState(character.Connection);
        return true;
    }

    /// <summary>
    /// Whether the character may carry out <paramref name="kind"/> right now. A refusal starts nothing: the
    /// window is read, not replaced, so a blocked attempt cannot push its expiry back and hold the account
    /// protected indefinitely. The refusal says so in chat, because the client has no dialog of its own for
    /// this and the second password's window is where the player can act on it.
    /// </summary>
    public static bool MayPerform(Character character, SensitiveOperationKind kind, out string reason)
    {
        reason = null;
        if (character?.Connection == null || !IsEnabled)
            return true;

        var accountId = character.Connection.AccountId;
        var state = StateFor(accountId, UtcNow);
        if (SensitiveOperationRules.MayPerform(true, state.Protected, false))
            return true;

        Logger.Info("{0} tried {1} while their account is protected", character.Name, kind);
        reason = "Your account is under protection: verify with your second password before this action.";
        return false;
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
        Logger.Info("{0} verified; sensitive-operation protection lifted", character.Name);
    }

    /// <summary>The state a GM surface reports: the window and whether the guard is on at all.</summary>
    public static string Describe(Character character)
    {
        if (character?.Connection == null)
            return "no character";

        var state = StateFor(character.Connection.AccountId, UtcNow);
        var enabled = IsEnabled ? "on" : "off (feature bit 56)";
        return state.Protected
            ? $"guard {enabled}; this account is protected for another {state.RemainSeconds}s"
            : $"guard {enabled}; this account is not protected";
    }
}

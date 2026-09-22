namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// A zone-permission ask the server opened for one character: which zone group asked, and when.
/// The ask is the authority <see cref="Core.Managers.IndunManager.AnswerZonePermission"/> checks —
/// an answer without one changes nothing.
/// </summary>
internal sealed class ZonePermissionSession(uint zoneGroupId, DateTime openedUtc)
{
    public uint ZoneGroupId { get; } = zoneGroupId;

    /// <summary>UTC open time from <see cref="ServerCalendar"/>; kept to age an ask that is never answered.</summary>
    public DateTime OpenedUtc { get; } = openedUtc;
}

/// <summary>How a zone-permission answer settled, before the handler maps it onto a result packet.</summary>
public enum ZonePermissionVerdict
{
    /// <summary>The ask existed and the client accepted (OK) — permission granted.</summary>
    Accepted,

    /// <summary>The ask existed and the client declined (Cancel) — permission not taken, ask closed.</summary>
    Declined,

    /// <summary>No open ask for this character: the answer is unsolicited and changes nothing.</summary>
    NoOpenAsk,

    /// <summary>The answer byte is neither of the two values the dialog can produce; state is untouched.</summary>
    Malformed,
}

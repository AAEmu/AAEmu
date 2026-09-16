namespace AAEmu.Game.Models.Game.Music;

/// <summary>What an invitation answer did, so the caller knows which packet to send and to whom.</summary>
public enum EnsembleJoinResult
{
    /// <summary>The player joined and the ensemble can go on.</summary>
    Accepted,

    /// <summary>The player turned it down; the leader is told.</summary>
    Rejected,

    /// <summary>The player is not part of this ensemble.</summary>
    NotMember,

    /// <summary>Every seat the client can draw is taken.</summary>
    Full,

    /// <summary>The ensemble is already playing, or already over.</summary>
    Closed
}

/// <summary>
/// An ensemble in the making: the player who called it (the maestro), the players asked, those who said
/// yes, and whose parts have arrived. The session knows nothing about packets or connections — it decides
/// who may join and when the performance can start, and the manager does the sending.
/// </summary>
public class EnsembleSession
{
    /// <summary>
    /// How many players an ensemble seats. The client's own ensemble packet draws at most five members,
    /// so a sixth would be written but never shown.
    /// </summary>
    public const int MaxMembers = 5;

    private readonly List<uint> _invited = [];
    private readonly List<uint> _members = [];
    private readonly HashSet<uint> _parts = [];

    public EnsembleSession(uint maestroBc, string maestroName)
    {
        MaestroBc = maestroBc;
        MaestroName = maestroName ?? string.Empty;
        _members.Add(maestroBc);
    }

    public uint MaestroBc { get; }

    public string MaestroName { get; }

    /// <summary>Players asked and not yet answered.</summary>
    public IReadOnlyList<uint> Invited => _invited;

    /// <summary>Players who said yes, the maestro first — the list the client renders.</summary>
    public IReadOnlyList<uint> Members => _members;

    /// <summary>Members whose part has arrived, by object id.</summary>
    public IReadOnlyCollection<uint> Parts => _parts;

    public bool IsStarted { get; private set; }

    public bool IsCanceled { get; private set; }

    public bool IsOpen => !IsStarted && !IsCanceled;

    /// <summary>Asks a player to join. The maestro is a member from the start and cannot be asked again.</summary>
    public bool Invite(uint bc)
    {
        if (!IsOpen || bc == 0 || bc == MaestroBc)
            return false;

        if (_members.Contains(bc) || _invited.Contains(bc))
            return false;

        // The maestro takes one of the seats the client can draw.
        if (_members.Count + _invited.Count >= MaxMembers)
            return false;

        _invited.Add(bc);
        return true;
    }

    /// <summary>Takes an invitation up. The maestro is seated from the start, so this is for invitees.</summary>
    public EnsembleJoinResult Accept(uint bc)
    {
        if (!IsOpen)
            return EnsembleJoinResult.Closed;

        if (_members.Contains(bc))
            return EnsembleJoinResult.Accepted;

        if (!_invited.Remove(bc))
            return EnsembleJoinResult.NotMember;

        if (_members.Count >= MaxMembers)
            return EnsembleJoinResult.Full;

        _members.Add(bc);
        return EnsembleJoinResult.Accepted;
    }

    /// <summary>Turns an invitation down.</summary>
    public EnsembleJoinResult Reject(uint bc)
    {
        if (!IsOpen)
            return EnsembleJoinResult.Closed;

        return _invited.Remove(bc) ? EnsembleJoinResult.Rejected : EnsembleJoinResult.NotMember;
    }

    /// <summary>Notes that a member's part has arrived. Returns false for anyone not in the ensemble.</summary>
    public bool PartReady(uint bc)
    {
        if (!IsOpen || !_members.Contains(bc))
            return false;

        return _parts.Add(bc);
    }

    /// <summary>True once every member has sent a part, which is what the performance waits for.</summary>
    public bool AllPartsReady => _members.Count > 0 && _members.All(_parts.Contains);

    /// <summary>Closes the session for playing. Only a session with every part in can start.</summary>
    public bool Start()
    {
        if (!IsOpen || !AllPartsReady)
            return false;

        IsStarted = true;
        return true;
    }

    /// <summary>Ends the session, whether it was played or given up on.</summary>
    public void Cancel()
    {
        IsCanceled = true;
        _invited.Clear();
    }

    /// <summary>A member walking away takes their part with them and drops the session if it was theirs.</summary>
    public bool Leave(uint bc)
    {
        _parts.Remove(bc);
        _invited.Remove(bc);

        if (bc == MaestroBc)
        {
            Cancel();
            return true;
        }

        return _members.Remove(bc);
    }

    /// <summary>The player this ensemble belongs to, invited or seated.</summary>
    public bool Involves(uint bc)
    {
        return bc == MaestroBc || _members.Contains(bc) || _invited.Contains(bc);
    }

    /// <summary>Everyone the manager has to talk to: the maestro, the members and the players still asked.</summary>
    public List<uint> Participants()
    {
        var all = new List<uint> { MaestroBc };
        all.AddRange(_members.Where(bc => bc != MaestroBc));
        all.AddRange(_invited);
        return all;
    }
}

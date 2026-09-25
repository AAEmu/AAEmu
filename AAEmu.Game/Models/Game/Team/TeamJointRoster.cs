namespace AAEmu.Game.Models.Game.Team;

/// <summary>
/// Ordered federation of independent raid teams. The joint id is allocated by the manager, not
/// inferred from a client field; team order is the stable position used by the client's raid tabs.
/// </summary>
public sealed class TeamJointRoster
{
    private readonly List<TeamJointRosterEntry> _entries = [];

    public TeamJointRoster(uint jointId, uint leaderTeamId)
    {
        JointId = jointId;
        LeaderTeamId = leaderTeamId;
    }

    public uint JointId { get; }
    public uint LeaderTeamId { get; }
    public IReadOnlyList<TeamJointRosterEntry> Entries => _entries;

    public int MemberCount => _entries.Sum(entry => entry.MemberCount);

    public bool Contains(uint teamId) => _entries.Any(entry => entry.TeamId == teamId);

    public uint GetOrder(uint teamId)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].TeamId == teamId)
                return checked((uint)(i + 1));
        }

        return 0;
    }

    public bool TryAdd(uint teamId, int memberCount, int memberLimit)
    {
        if (teamId == 0 || memberCount < 0 || memberLimit <= 0 || Contains(teamId))
            return false;
        if (MemberCount > memberLimit - memberCount)
            return false;

        // The first team added is the leader and must be the one the manager declared. A follower
        // can never be inserted ahead of it, which is what gives the client's raid tabs a stable order.
        var isLeader = _entries.Count == 0;
        if (isLeader != (teamId == LeaderTeamId))
            return false;

        _entries.Add(new TeamJointRosterEntry(teamId, memberCount, isLeader));
        return true;
    }

    public bool Remove(uint teamId)
    {
        var index = _entries.FindIndex(entry => entry.TeamId == teamId);
        if (index < 0)
            return false;

        _entries.RemoveAt(index);
        if (index == 0 && _entries.Count > 0)
        {
            // A federation cannot silently promote a different team to leader. The manager
            // dissolves it instead when the leader disappears.
            throw new InvalidOperationException("The joint leader must be removed by the joint manager.");
        }

        return true;
    }

    public bool IsLeader(uint teamId) => _entries.Count > 0 && _entries[0].TeamId == teamId;

    public uint GetOtherTeamId(uint teamId)
    {
        var entry = _entries.FirstOrDefault(value => value.TeamId != teamId);
        return entry.TeamId;
    }
}

public readonly record struct TeamJointRosterEntry(uint TeamId, int MemberCount, bool IsLeader);

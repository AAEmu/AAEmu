using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>In-memory stand-in for the World so joint/summon flows run without a live server.</summary>
internal sealed class FakeTeamJointContext : ITeamJointContext
{
    private readonly Dictionary<uint, TeamJointTeamSnapshot> _teams = new();
    private readonly Dictionary<uint, TeamJointCharacterSnapshot> _characters = new();

    public List<(uint CharacterId, GamePacket Packet)> Sent { get; } = [];
    public List<(uint CharacterId, ErrorMessageType Error)> Errors { get; } = [];
    public List<(uint TeamId, uint RecipientId)> HeadersSent { get; } = [];
    public sbyte LocalWorldId { get; set; }

    public FakeTeamJointContext AddTeam(uint teamId, uint ownerId, bool isParty = false, params uint[] members)
    {
        var online = members.Length == 0 ? [ownerId] : members;
        _teams[teamId] = new TeamJointTeamSnapshot(teamId, isParty, ownerId, 0, online.Length, online, 0, false, 0);
        return this;
    }

    public FakeTeamJointContext SetOfficer(uint teamId, ulong officerId)
    {
        var team = _teams[teamId];
        _teams[teamId] = team with { OfficerId = officerId };
        return this;
    }

    public FakeTeamJointContext AddCharacter(uint id, string name, bool isOnline = true, bool isInBattle = false)
    {
        _characters[id] = new TeamJointCharacterSnapshot(id, name, isOnline, isInBattle, 133u, 10f, 20f, 30f);
        return this;
    }

    public FakeTeamJointContext GoOffline(uint id) => SetOnline(id, false);

    public FakeTeamJointContext SetOnline(uint id, bool online)
    {
        _characters[id] = _characters[id] with { IsOnline = online };
        return this;
    }

    public FakeTeamJointContext SetInBattle(uint id, bool inBattle)
    {
        _characters[id] = _characters[id] with { IsInBattle = inBattle };
        return this;
    }

    public FakeTeamJointContext RemoveTeam(uint teamId)
    {
        _teams.Remove(teamId);
        return this;
    }

    public TeamJointTeamSnapshot? Team(uint teamId) => _teams.GetValueOrDefault(teamId);

    public int CountPackets<T>() where T : GamePacket => Sent.Count(entry => entry.Packet is T);

    public IReadOnlyList<T> PacketsTo<T>(uint characterId) where T : GamePacket =>
        Sent.Where(entry => entry.CharacterId == characterId && entry.Packet is T).Select(entry => (T)entry.Packet).ToArray();

    public TeamJointTeamSnapshot? FindTeam(uint teamId) => _teams.GetValueOrDefault(teamId);

    public TeamJointTeamSnapshot? FindTeamByMember(uint unitId) =>
        _teams.Values.FirstOrDefault(team => team.OnlineMemberIds.Contains(unitId));

    public TeamJointCharacterSnapshot? FindCharacterByName(string name) =>
        _characters.Values.FirstOrDefault(character => character.Name == name);

    public TeamJointCharacterSnapshot? FindCharacterById(uint characterId) => _characters.GetValueOrDefault(characterId);

    public bool IsLocalWorld(sbyte worldId) => worldId == LocalWorldId;

    public void Send(uint characterId, GamePacket packet) => Sent.Add((characterId, packet));

    public void SendError(uint characterId, ErrorMessageType error) => Errors.Add((characterId, error));

    public void SendTeamHeader(uint teamId, uint recipientId) => HeadersSent.Add((teamId, recipientId));

    public void ApplyJoint(uint teamId, uint jointId, bool isLeader, int order) =>
        _teams[teamId] = _teams[teamId] with { JointId = jointId, IsJointLeader = isLeader, JointOrder = order };

    public void ClearJoint(uint teamId) =>
        _teams[teamId] = _teams[teamId] with { JointId = 0, IsJointLeader = false, JointOrder = 0 };
}

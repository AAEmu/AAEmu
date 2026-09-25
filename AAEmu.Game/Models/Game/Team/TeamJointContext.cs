using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Models.Game.Team;

/// <summary>Everything the joint manager needs to know about one team, without a live Team object.</summary>
public sealed record TeamJointTeamSnapshot(
    uint Id,
    bool IsParty,
    uint OwnerId,
    ulong OfficerId,
    int MemberCount,
    uint[] OnlineMemberIds,
    uint JointId,
    bool IsJointLeader,
    int JointOrder)
{
    public bool IsRaid => !IsParty;

    public bool CanManage(uint characterId) =>
        !IsParty && (OwnerId == characterId || OfficerId == characterId);

    public bool CanBreak(uint characterId) => IsJointLeader && CanManage(characterId);

    public bool HasOnlineMembersExcept(uint characterId) =>
        Array.Exists(OnlineMemberIds, id => id != characterId);
}

/// <summary>Everything the joint manager needs to know about one character, without a live unit.</summary>
public sealed record TeamJointCharacterSnapshot(
    uint Id,
    string Name,
    bool IsOnline,
    bool IsInBattle,
    uint ZoneId,
    float X,
    float Y,
    float Z);

/// <summary>
/// World-side access the joint/summon flows need. The live implementation reads TeamManager and
/// IWorldManager; tests supply a fake so every flow is deterministic without a running World.
/// </summary>
public interface ITeamJointContext
{
    TeamJointTeamSnapshot? FindTeam(uint teamId);
    TeamJointTeamSnapshot? FindTeamByMember(uint unitId);
    TeamJointCharacterSnapshot? FindCharacterByName(string name);
    TeamJointCharacterSnapshot? FindCharacterById(uint characterId);
    bool IsLocalWorld(sbyte worldId);
    void Send(uint characterId, GamePacket packet);
    void SendError(uint characterId, ErrorMessageType error);
    void SendTeamHeader(uint teamId, uint recipientId);
    void ApplyJoint(uint teamId, uint jointId, bool isLeader, int order);
    void ClearJoint(uint teamId);
}

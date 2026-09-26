using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Managers;

/// <summary>Live <see cref="ITeamJointContext"/> over TeamManager and the World character registry.</summary>
public sealed class WorldTeamJointContext(IWorldManager worldManager, ITeamManager teamManager) : ITeamJointContext
{
    public TeamJointTeamSnapshot? FindTeam(uint teamId)
    {
        var team = teamManager.GetActiveTeam(teamId);
        return team == null ? null : Snapshot(team);
    }

    public TeamJointTeamSnapshot? FindTeamByMember(uint unitId)
    {
        var team = teamManager.GetActiveTeamByUnit(unitId);
        return team == null ? null : Snapshot(team);
    }

    public TeamJointCharacterSnapshot? FindCharacterByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return Snapshot(worldManager.GetCharacter(name));
    }

    public TeamJointCharacterSnapshot? FindCharacterById(uint characterId) =>
        Snapshot(worldManager.GetCharacterById(characterId));

    public TeamJointCharacterSnapshot? FindSelectedTarget(uint characterId)
    {
        var character = worldManager.GetCharacterById(characterId);
        // Only a live character target can name another character; anything else (an npc, a
        // doodad, nothing) cannot start a joint, so it is reported as no target.
        return Snapshot(character?.CurrentTarget as Character);
    }

    public bool IsLocalWorld(sbyte worldId) =>
        worldId == CharacterBlocked.LocalWorldId || unchecked((byte)worldId) == AppConfiguration.Instance.Id;

    public void Send(uint characterId, GamePacket packet) =>
        worldManager.GetCharacterById(characterId)?.SendPacket(packet);

    public void SendError(uint characterId, ErrorMessageType error) =>
        worldManager.GetCharacterById(characterId)?.SendErrorMessage(error);

    public void SendTeamHeader(uint teamId, uint recipientId)
    {
        var team = teamManager.GetActiveTeam(teamId);
        if (team != null)
            Send(recipientId, new SCJoinedTeamPacket(team));
    }

    public void ApplyJoint(uint teamId, uint jointId, bool isLeader, int order)
    {
        var team = teamManager.GetActiveTeam(teamId);
        if (team == null)
            return;
        team.JointId = jointId;
        team.IsJointLeader = isLeader;
        team.JointOrder = order;
    }

    public void ClearJoint(uint teamId)
    {
        var team = teamManager.GetActiveTeam(teamId);
        if (team == null)
            return;
        team.JointId = 0;
        team.IsJointLeader = false;
        team.JointOrder = 0;
    }

    private static TeamJointTeamSnapshot Snapshot(Team team) =>
        new(team.Id,
            team.IsParty,
            team.OwnerId,
            team.OfficerId,
            team.MembersCount(),
            team.Members
                .Where(member => member?.Character is { IsOnline: true })
                .Select(member => member.Character.Id)
                .ToArray(),
            team.JointId,
            team.IsJointLeader,
            team.JointOrder);

    private static TeamJointCharacterSnapshot? Snapshot(Character character)
    {
        if (character == null)
            return null;
        var position = character.Transform.World.Position;
        return new TeamJointCharacterSnapshot(
            character.Id,
            character.Name,
            character.IsOnline,
            character.IsInBattle,
            character.Transform.ZoneId,
            position.X,
            position.Y,
            position.Z);
    }
}

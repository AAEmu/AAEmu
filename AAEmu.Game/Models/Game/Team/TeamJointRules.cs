namespace AAEmu.Game.Models.Game.Team;

/// <summary>Authorization and capacity checks shared by the joint/summon packet handlers.</summary>
public static class TeamJointRules
{
    public static bool IsRaid(Team team) => team is { IsParty: false };

    public static bool CanManage(Team team, uint characterId) =>
        team is { } value && IsRaid(value) && (value.OwnerId == characterId || value.IsOfficer(characterId));

    public static bool CanBreak(Team team, uint characterId) =>
        team is { } value && value.IsJointLeader && CanManage(value, characterId);

    public static bool Fits(int firstCount, int secondCount, int memberLimit) =>
        firstCount >= 0 && secondCount >= 0 && memberLimit >= 0 &&
        firstCount <= memberLimit && secondCount <= memberLimit - firstCount;
}

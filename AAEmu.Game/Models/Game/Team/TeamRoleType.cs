namespace AAEmu.Game.Models.Game.Team;

/// <summary>
/// Native team kind used by the 10.0.2.13 invitation protocol.
/// </summary>
public enum TeamRoleType : sbyte
{
    Solo = 0,
    Party = 1,
    Raid = 2,
    SiegeRaid = 3
}

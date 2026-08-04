namespace AAEmu.Game.Models.Game.DoodadObj.Static;

public enum DoodadFuncPermission : byte
{
    // 10.0.2 content DB enum_doodad_perms / doodad_funcs.perm_id.
    Public = 0,
    Owner = 1,
    Friend = 2,
    SiegeMaster = 3,
    Party = 4,
    Raid = 5,
    Account = 6,
    ZoneCitizen = 7,
    HouseInZone = 8,
    Expedition = 9,
    Family = 10,
    Nation = 11,
    FriendlyFaction = 12,
    FirstInteraction = 13,
    PartyOwner = 14,
    RaidOwner = 15,
}

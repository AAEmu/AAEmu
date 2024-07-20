namespace AAEmu.Game.Models.Game.DoodadObj.Static;

public enum DoodadFuncPermission : byte
{
    // TODO: complete and verify enums
    Any = 0,
    Permission1 = 1,
    Permission2 = 2, // seems to be used for one type of uproot only, maybe family permission?
    OwnerOnly = 3, // for recover furniture?
    Permission4 = 4, // seems to be event related only
    OwnerRaidMembers = 5,
    SameAccount = 6,
    ZoneResidents = 8,
}

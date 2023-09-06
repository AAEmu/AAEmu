using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CharacterEquipPack
{
    public long? Id { get; set; }

    public long? AbilityId { get; set; }

    public long? NewbieClothPackId { get; set; }

    public long? NewbieWeaponPackId { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Character
{
    public long? Id { get; set; }

    public long? CharRaceId { get; set; }

    public long? CharGenderId { get; set; }

    public long? ModelId { get; set; }

    public long? FactionId { get; set; }

    public long? StartingZoneId { get; set; }

    public long? PreviewBodyPackId { get; set; }

    public long? PreviewClothPackId { get; set; }

    public long? DefaultReturnDistrictId { get; set; }

    public long? DefaultResurrectionDistrictId { get; set; }

    public long? DefaultSystemVoiceSoundPackId { get; set; }

    public long? DefaultFxVoiceSoundPackId { get; set; }

    public byte[] Creatable { get; set; }
}

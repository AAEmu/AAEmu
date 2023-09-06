using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CombatSound
{
    public long? Id { get; set; }

    public long? SourceSoundMaterialId { get; set; }

    public long? TargetSoundMaterialId { get; set; }

    public long? SoundId { get; set; }

    public long? FxGroupId { get; set; }
}

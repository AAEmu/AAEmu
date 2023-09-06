using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WearableKind
{
    public long? Id { get; set; }

    public long? ArmorTypeId { get; set; }

    public long? ArmorRatio { get; set; }

    public long? MagicResistanceRatio { get; set; }

    public long? FullBuffId { get; set; }

    public long? HalfBuffId { get; set; }

    public long? SoundMaterialId { get; set; }

    public long? ExtraDamagePierce { get; set; }

    public long? ExtraDamageSlash { get; set; }

    public long? ExtraDamageBlunt { get; set; }

    public double? DurabilityRatio { get; set; }
}

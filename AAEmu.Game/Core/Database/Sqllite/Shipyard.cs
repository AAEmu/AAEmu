using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Shipyard
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? MainModelId { get; set; }

    public long? ItemId { get; set; }

    public long? CeremonyModelId { get; set; }

    public string CeremonyAnimKey { get; set; }

    public long? CeremonyAnimTime { get; set; }

    public double? SpawnOffsetFront { get; set; }

    public double? SpawnOffsetZ { get; set; }

    public long? BuildRadius { get; set; }

    public long? TaxDuration { get; set; }

    public long? OriginItemId { get; set; }

    public long? TaxationId { get; set; }
}

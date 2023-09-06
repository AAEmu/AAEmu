using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class HousingDecoration
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public byte[] AllowOnFloor { get; set; }

    public byte[] AllowOnWall { get; set; }

    public byte[] AllowOnCeiling { get; set; }

    public long? DoodadId { get; set; }

    public byte[] AllowPivotOnGarden { get; set; }

    public long? ActabilityGroupId { get; set; }

    public long? ActabilityUp { get; set; }

    public long? DecoActabilityGroupId { get; set; }

    public byte[] AllowMeshOnGarden { get; set; }
}

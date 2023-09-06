using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FxItem
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string AssetName { get; set; }

    public long? FxEventStartId { get; set; }

    public long? FxEventEndId { get; set; }

    public long? FxLocationId { get; set; }

    public long? BoneId { get; set; }

    public double? OffsetX { get; set; }

    public double? OffsetY { get; set; }

    public double? OffsetZ { get; set; }

    public long? FxDetailId { get; set; }

    public string FxDetailType { get; set; }

    public long? OffsetAxisId { get; set; }

    public long? FxScaleId { get; set; }
}

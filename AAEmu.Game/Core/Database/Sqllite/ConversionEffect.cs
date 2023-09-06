using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ConversionEffect
{
    public long? Id { get; set; }

    public long? CategoryId { get; set; }

    public long? SourceCategoryId { get; set; }

    public long? SourceValue { get; set; }

    public long? TargetCategoryId { get; set; }

    public long? TargetValue { get; set; }
}

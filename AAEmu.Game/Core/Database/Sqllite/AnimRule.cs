using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AnimRule
{
    public long? Id { get; set; }

    public long? FirstCategoryId { get; set; }

    public long? SecondCategoryId { get; set; }

    public long? Before { get; set; }

    public long? BeforeOperatorId { get; set; }

    public long? DefaultOperatorId { get; set; }
}

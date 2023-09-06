using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BuffTolerance
{
    public long? Id { get; set; }

    public long? BuffTagId { get; set; }

    public long? StepDuration { get; set; }

    public long? FinalStepBuffId { get; set; }

    public long? CharacterTimeReduction { get; set; }
}

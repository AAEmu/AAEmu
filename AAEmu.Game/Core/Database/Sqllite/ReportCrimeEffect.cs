using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ReportCrimeEffect
{
    public long? Id { get; set; }

    public long? Value { get; set; }

    public long? CrimeKindId { get; set; }
}

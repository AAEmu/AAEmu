using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CinemaCaption
{
    public long? Id { get; set; }

    public long? CinemaId { get; set; }

    public string Caption { get; set; }
}

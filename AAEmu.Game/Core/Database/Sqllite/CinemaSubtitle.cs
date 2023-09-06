using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CinemaSubtitle
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Subtitle { get; set; }
}

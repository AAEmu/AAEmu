using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class EmblemPattern
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? KindId { get; set; }

    public string Path { get; set; }

    public string IconPath { get; set; }
}

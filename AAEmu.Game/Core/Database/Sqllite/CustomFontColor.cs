using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class CustomFontColor
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? Color { get; set; }
}

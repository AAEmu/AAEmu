using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class UiText
{
    public long? Id { get; set; }

    public string Key { get; set; }

    public string Text { get; set; }

    public long? CategoryId { get; set; }
}

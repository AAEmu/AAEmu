using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class District
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? KindId { get; set; }

    public long? QuestCategoryId { get; set; }
}

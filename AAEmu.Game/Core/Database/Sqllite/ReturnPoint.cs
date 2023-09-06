using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ReturnPoint
{
    public long? Id { get; set; }

    public string EditorName { get; set; }

    public string Name { get; set; }

    public long? QuestCategoryId { get; set; }

    public long? MilestoneId { get; set; }
}

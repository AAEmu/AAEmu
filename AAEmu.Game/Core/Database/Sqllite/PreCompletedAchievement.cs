using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class PreCompletedAchievement
{
    public long? Id { get; set; }

    public long? MyAchievementId { get; set; }

    public long? CompletedAchievementId { get; set; }
}

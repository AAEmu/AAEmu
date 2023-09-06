using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Achievement
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string Summary { get; set; }

    public string Description { get; set; }

    public long? CategoryId { get; set; }

    public long? SubCategoryId { get; set; }

    public long? ParentAchievementId { get; set; }

    public byte[] IsActive { get; set; }

    public byte[] IsHidden { get; set; }

    public long? Priority { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public byte[] CompleteOr { get; set; }

    public long? CompleteNum { get; set; }

    public long? ItemId { get; set; }

    public long? IconId { get; set; }
}

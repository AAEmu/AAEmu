using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ModelQuestCamera
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public long? QuestCameraId { get; set; }
}

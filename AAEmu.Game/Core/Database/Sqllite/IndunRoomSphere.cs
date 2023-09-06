using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IndunRoomSphere
{
    public long? Id { get; set; }

    public long? CenterDoodadId { get; set; }

    public long? Radius { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IndunActionSetRoomCleared
{
    public long? Id { get; set; }

    public long? IndunRoomId { get; set; }
}

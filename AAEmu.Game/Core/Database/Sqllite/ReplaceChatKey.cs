using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ReplaceChatKey
{
    public long? Id { get; set; }

    public long? ReplaceChatId { get; set; }

    public string Key { get; set; }
}

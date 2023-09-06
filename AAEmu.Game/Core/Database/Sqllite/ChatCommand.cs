using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ChatCommand
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ChatTypeId { get; set; }

    public long? MenuOrder { get; set; }
}

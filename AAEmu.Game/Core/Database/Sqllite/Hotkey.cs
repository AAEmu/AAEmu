using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Hotkey
{
    public long? Id { get; set; }

    public long? ActionId { get; set; }

    public long? ActionType1Id { get; set; }

    public long? ActionType2Id { get; set; }

    public long? CategoryId { get; set; }

    public long? ModeId { get; set; }

    public string KeyPrimary { get; set; }

    public string KeySecond { get; set; }

    public string Activation { get; set; }
}

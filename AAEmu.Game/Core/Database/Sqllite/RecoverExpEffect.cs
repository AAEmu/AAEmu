using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class RecoverExpEffect
{
    public long? Id { get; set; }

    public byte[] NeedMoney { get; set; }

    public byte[] NeedLaborPower { get; set; }

    public byte[] NeedPriest { get; set; }

    public byte[] Penaltied { get; set; }
}

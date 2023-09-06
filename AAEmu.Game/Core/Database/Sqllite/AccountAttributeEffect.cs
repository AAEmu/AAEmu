using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AccountAttributeEffect
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public byte[] BindWorld { get; set; }

    public byte[] IsAdd { get; set; }

    public long? Count { get; set; }

    public long? Time { get; set; }
}

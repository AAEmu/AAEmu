using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FxChr
{
    public long? Id { get; set; }

    public byte[] BindToBoneAfterEnd { get; set; }
}

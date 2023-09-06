using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DdcmsMergeProtectInfo
{
    public long? Id { get; set; }

    public string TblName { get; set; }

    public string TblColumnName { get; set; }

    public long? Idx { get; set; }

    public long? Action { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillReq
{
    public long? Id { get; set; }

    public byte[] Target { get; set; }

    public long? BuffId { get; set; }

    public long? BuffTagId { get; set; }

    public string Message { get; set; }

    public byte[] DefaultResult { get; set; }
}

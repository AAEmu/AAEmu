using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FaceEyelashMap
{
    public long? Id { get; set; }

    public long? ModelId { get; set; }

    public string Name { get; set; }

    public string Eyelash { get; set; }

    public byte[] NpcOnly { get; set; }
}

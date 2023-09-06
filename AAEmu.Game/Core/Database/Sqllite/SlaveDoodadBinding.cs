using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlaveDoodadBinding
{
    public long? Id { get; set; }

    public long? OwnerId { get; set; }

    public string OwnerType { get; set; }

    public long? AttachPointId { get; set; }

    public long? DoodadId { get; set; }

    public byte[] Persist { get; set; }

    public double? Scale { get; set; }
}

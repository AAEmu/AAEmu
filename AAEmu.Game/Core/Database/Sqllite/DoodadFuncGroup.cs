using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncGroup
{
    public long? Id { get; set; }

    public string Model { get; set; }

    public long? DoodadAlmightyId { get; set; }

    public long? DoodadFuncGroupKindId { get; set; }

    public string PhaseMsg { get; set; }

    public long? SoundId { get; set; }

    public string Name { get; set; }

    public long? SoundTime { get; set; }

    public string Comment { get; set; }

    public byte[] IsMsgToZone { get; set; }
}

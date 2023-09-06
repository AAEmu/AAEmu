using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class IndunZone
{
    public long? ZoneGroupId { get; set; }

    public string Name { get; set; }

    public string Comment { get; set; }

    public long? LevelMin { get; set; }

    public long? LevelMax { get; set; }

    public long? MaxPlayers { get; set; }

    public byte[] Pvp { get; set; }

    public byte[] HasGraveyard { get; set; }

    public long? ItemId { get; set; }

    public long? RestoreItemTime { get; set; }

    public byte[] PartyOnly { get; set; }

    public byte[] ClientDriven { get; set; }

    public byte[] SelectChannel { get; set; }
}

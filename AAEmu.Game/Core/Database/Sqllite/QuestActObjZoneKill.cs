using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActObjZoneKill
{
    public long? Id { get; set; }

    public long? CountPk { get; set; }

    public long? CountNpc { get; set; }

    public long? ZoneId { get; set; }

    public byte[] TeamShare { get; set; }

    public byte[] UseAlias { get; set; }

    public long? QuestActObjAliasId { get; set; }

    public long? LvMin { get; set; }

    public long? LvMax { get; set; }

    public byte[] IsParty { get; set; }

    public long? LvMinNpc { get; set; }

    public long? LvMaxNpc { get; set; }

    public long? PcFactionId { get; set; }

    public byte[] PcFactionExclusive { get; set; }

    public long? NpcFactionId { get; set; }

    public byte[] NpcFactionExclusive { get; set; }
}

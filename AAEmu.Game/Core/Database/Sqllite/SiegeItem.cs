using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SiegeItem
{
    public long? Id { get; set; }

    public byte[] OutsideSiegeZone { get; set; }

    public byte[] DuringNoDominion { get; set; }

    public byte[] DuringDeclare { get; set; }

    public byte[] DuringPeace { get; set; }

    public byte[] OutsideSiegeAreaDuringWarmup { get; set; }

    public byte[] OutsideSiegeAreaDuringSiege { get; set; }

    public byte[] OffenseHqDuringWarmup { get; set; }

    public byte[] OffenseHqDuringSiege { get; set; }

    public byte[] DefenseHqDuringWarmup { get; set; }

    public byte[] DefenseHqDuringSiege { get; set; }

    public byte[] SiegeCircleDuringWarmup { get; set; }

    public byte[] SiegeCircleDuringSiege { get; set; }

    public string Usage { get; set; }
}

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

public partial class Crime
{
    public uint Id { get; set; }

    public uint CriminalId { get; set; }

    public string CriminalName { get; set; }

    public uint ReporterId { get; set; }

    public string ReporterName { get; set; }

    public uint Type3 { get; set; }

    public uint Type4 { get; set; }

    public sbyte CrimeKind { get; set; }

    public uint Type5 { get; set; }

    public uint Type6 { get; set; }

    public long X { get; set; }

    public long Y { get; set; }

    public float Z { get; set; }

    public string Desc { get; set; }

    public DateTime Time { get; set; }
}

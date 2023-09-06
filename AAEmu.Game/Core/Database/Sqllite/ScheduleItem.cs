using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ScheduleItem
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? KindId { get; set; }

    public long? StYear { get; set; }

    public long? StMonth { get; set; }

    public long? StDay { get; set; }

    public long? StHour { get; set; }

    public long? StMin { get; set; }

    public long? EdYear { get; set; }

    public long? EdMonth { get; set; }

    public long? EdDay { get; set; }

    public long? EdHour { get; set; }

    public long? EdMin { get; set; }

    public long? GiveTerm { get; set; }

    public long? GiveMax { get; set; }

    public long? ItemId { get; set; }

    public long? ItemCount { get; set; }

    public long? PremiumGradeId { get; set; }

    public byte[] ActiveTake { get; set; }

    public byte[] OnAir { get; set; }

    public string ToolTip { get; set; }

    public byte[] ShowWherever { get; set; }

    public byte[] ShowWhenever { get; set; }

    public string IconPath { get; set; }

    public string EnableKeyString { get; set; }

    public string DisableKeyString { get; set; }

    public string LabelKeyString { get; set; }
}

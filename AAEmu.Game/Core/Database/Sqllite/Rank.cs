using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Rank
{
    public long? Id { get; set; }

    public string Name { get; set; }

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

    public long? DayOfWeekId { get; set; }

    public long? StartTime { get; set; }

    public long? EndTime { get; set; }

    public long? ZoneGroupId { get; set; }

    public long? RankKindId { get; set; }

    public long? ResetWeek { get; set; }

    public string V1 { get; set; }

    public string V2 { get; set; }

    public long? StartTimeAlarm { get; set; }

    public long? EndTimeAlarm { get; set; }

    public string StartTimeAlarmMsg { get; set; }

    public string EndTimeAlarmMsg { get; set; }
}

namespace AAEmu.Game.Models.Game.Schedules;

public class ScheduleItems
{
    public uint Id { get; set; }
    public bool ActiveTake { get; set; }
    public uint AutoTakeDelay { get; set; }
    public string DisableKeyString { get; set; }
    public uint EdDay { get; set; }
    public uint EdHour { get; set; }
    public uint EdMin { get; set; }
    public uint EdMonth { get; set; }
    public uint EdYear { get; set; }
    public string EnableKeyString { get; set; }
    public uint GiveMax { get; set; }
    public uint GiveTerm { get; set; }
    public string IconPath { get; set; }
    public uint ItemCount { get; set; }
    public uint ItemId { get; set; }
    public uint KindId { get; set; }
    public uint KindValue { get; set; }
    public string LabelKeyString { get; set; }
    public string MailBody { get; set; }
    public string MailTitle { get; set; }
    public string Name { get; set; }
    public bool OnAir { get; set; }
    public bool ShowWhenever { get; set; }
    public bool ShowWherever { get; set; }
    public uint StDay { get; set; }
    public uint StHour { get; set; }
    public uint StMin { get; set; }
    public uint StMonth { get; set; }
    public uint StYear { get; set; }
    public bool ToolTip { get; set; }
    public bool WheneverTooltip { get; set; }
    public bool WhereverTooltip { get; set; }
}

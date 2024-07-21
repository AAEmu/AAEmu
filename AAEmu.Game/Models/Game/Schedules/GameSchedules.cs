namespace AAEmu.Game.Models.Game.Schedules;

public class GameSchedules
{
    public int Id { get; set; } // GameScheduleId
    public string Name { get; set; }
    public DayOfWeek DayOfWeekId { get; set; }
    public int StartTime { get; set; }
    public int EndTime { get; set; }
    public int StYear { get; set; }
    public int StMonth { get; set; }
    public int StDay { get; set; }
    public int StHour { get; set; }
    public int StMin { get; set; }
    public int EdYear { get; set; }
    public int EdMonth { get; set; }
    public int EdDay { get; set; }
    public int EdHour { get; set; }
    public int EdMin { get; set; }
    public int StartTimeMin { get; set; }
    public int EndTimeMin { get; set; }
}

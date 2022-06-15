namespace AAEmu.Game.Models.Game.Schedules
{
    public enum DayOfWeek : int
    {
        Sunday = 0x1,
        Monday = 0x2,
        Tuesday = 0x3,
        Wednesday = 0x4,
        Thursday = 0x5,
        Friday = 0x6,
        Saturday = 0x7,
        Invalid = 0x8
    }

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
}

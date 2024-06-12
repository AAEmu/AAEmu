namespace AAEmu.Game.Models.Game.Attendance;

public class AccountAttendanceReward
{
    public uint Id { get; set; }
    public bool AdditionalReward { get; set; }
    public int DayCount { get; set; }
    public int ItemCount { get; set; }
    public int ItemGradeId { get; set; }
    public uint ItemId { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }


    public AccountAttendanceReward()
    {
    }
}

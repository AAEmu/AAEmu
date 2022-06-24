using System;

namespace AAEmu.Game.IO
{
    public class DateTimeManager : IDateTimeManager
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}

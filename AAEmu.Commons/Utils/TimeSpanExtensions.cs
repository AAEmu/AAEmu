using System;

namespace AAEmu.Commons.Utils
{
    public static class TimeSpanExtensions
    {
        public static bool IsBetween(this TimeSpan time, TimeSpan startTime, TimeSpan endTime)
        {
            if (endTime == startTime)
            {
                return true;
            }

            if (endTime < startTime)
            {
                return time <= endTime || time >= startTime;
            }

            return time >= startTime && time <= endTime;
        }
    }
}

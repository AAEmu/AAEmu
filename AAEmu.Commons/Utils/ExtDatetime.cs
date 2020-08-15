using System;
/*
  That file part of Code Monsters framework.
  Cerium Unity 2015 © 
*/
namespace Commons.Utils
{
    public static class ExtDatetime
    {
        private static readonly DateTime UnixTime = new DateTime(1970, 1, 1);

        public static long UnixSeconds(this DateTime date)
        {
            if (date < UnixTime)
                return -1L;
            return (long)date.ToUniversalTime().Subtract(UnixTime).TotalSeconds;
        }

        public static long UnixMilliseconds(this DateTime date)
        {
            if (date < UnixTime)
                return -1L;
            return (long)date.ToUniversalTime().Subtract(UnixTime).TotalMilliseconds;
        }

        /// <summary>
        /// Property, that return current rounded int UTC time
        /// </summary>
        public static int RoundedUtc => (int)(Utc / 1000);

        /// <summary>
        /// Property, that return current long UTC time
        /// </summary>
        public static long Utc => (long)(DateTime.UtcNow - UnixTime).TotalMilliseconds;
    }
}

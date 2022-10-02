using System;

namespace AAEmu.Commons.Utils
{
    public static class Rand
    {
        private static MersenneTwister _random = new MersenneTwister(DateTime.UtcNow.Millisecond);
        private static object _lock = new object();

        public static int Next()
        {
            lock (_lock)
            {
                return _random.Next();
            }
        }

        public static int Next(int maxValue)
        {
            lock (_lock)
            {
                return _random.Next(maxValue);
            }
        }

        public static int Next(int minValue, int maxValue)
        {
            lock (_lock)
            {
                return _random.Next(minValue, maxValue);
            }
        }

        public static double NextDouble()
        {
            lock (_lock)
            {
                return _random.NextDouble(true);
            }
        }

        public static float NextSingle()
        {
            lock (_lock)
            {
                return _random.NextSingle(true);
            }
        }

        public static float Next(float maxValue)
        {
            lock (_lock)
            {
                return _random.NextSingle(true) * maxValue;
            }
        }

        public static float Next(float minValue, float maxValue)
        {
            lock (_lock)
            {
                return _random.NextSingle(true) * (maxValue - minValue) + minValue;
            }
        }
    }
}

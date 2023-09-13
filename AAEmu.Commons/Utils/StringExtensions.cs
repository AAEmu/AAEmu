using System;

namespace AAEmu.Commons.Utils
{
    public static class StringExtensions
    {
        public static string FirstCharToUpper(this string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException($"{nameof(input)} is null or empty");

            return char.ToUpper(input[0]) + input[1..];
        }
    }
}

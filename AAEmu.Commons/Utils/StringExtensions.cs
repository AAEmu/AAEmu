using System;

namespace AAEmu.Commons.Utils
{
    public static class StringExtensions
    {
        public static string FirstCharToUpper(this string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentNullException($"{nameof(input)} is null or empty");
            return input[0].ToString().ToUpper() + input.Substring(1);
        }
    }
}

using System;

namespace AAEmu.Commons.Utils;

public static class StringExtensions
{
    /// <summary>
    /// Makes the first character of the String uppercase
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static string FirstCharToUpper(this string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException($"{nameof(input)} is null or empty");

        return char.ToUpper(input[0]) + input[1..];
    }

    /// <summary>
    /// Makes the first character of the String uppercase and the rest lowercase 
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string NormalizeName(this string input)
    {
        var trimmed = input.AsSpan().Trim();
        
        // Ignore if it's just whitespace and return the original
        if (trimmed.Length == 0)
        {
            return input;
        }

        Span<char> output = stackalloc char[trimmed.Length];
        output[0] = char.ToUpper(trimmed[0]);
        if (trimmed.Length > 1)
        {
            trimmed[1..].ToLower(output[1..], System.Globalization.CultureInfo.CurrentCulture);
        }

        return output.ToString();
    }
}

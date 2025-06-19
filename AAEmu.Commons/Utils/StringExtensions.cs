using System;

namespace AAEmu.Commons.Utils;

/// <summary>
/// 提供对 <see cref="string"/> 类型的扩展方法。
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// 将字符串的第一个字符转换为大写
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
    /// 规范化名称字符串，将其两端的空白移除后，首字母大写，其余字母小写。
    /// 例如："  nAmE  " 将变为 "Name"。
    /// </summary>
    /// <param name="input">要规范化的输入字符串。</param>
    /// <returns>规范化后的字符串；如果原始字符串在移除空白后为空，则返回原始字符串。</returns>
    public static string NormalizeName(this string input)
    {
        var trimmed = input.AsSpan().Trim(); // 使用 AsSpan().Trim() 以避免分配新的字符串用于修剪。
        
        // 如果只是空白字符，则忽略并返回原始字符串
        if (trimmed.Length == 0)
        {
            return input;
        }

        // 使用 stackalloc 在栈上分配字符数组，以提高性能并减少堆分配，适用于长度较短的字符串。
        Span<char> output = stackalloc char[trimmed.Length];
        output[0] = char.ToUpper(trimmed[0]);
        if (trimmed.Length > 1)
        {
            trimmed[1..].ToLower(output[1..], System.Globalization.CultureInfo.CurrentCulture);
        }

        return output.ToString();
    }
}

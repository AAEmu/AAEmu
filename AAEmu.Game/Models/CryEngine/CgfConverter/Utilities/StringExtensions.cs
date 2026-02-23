namespace CgfConverter.Utils;

public static class StringExtensions
{
    public static bool IsTrueyString(this string s, bool emptyIsTrue = false) => s.ToLowerInvariant() switch
    {
        "" => emptyIsTrue,
        "t" => true,
        "true" => true,
        "1" => true,
        "y" => true,
        "yes" => true,
        _ => false
    };
}
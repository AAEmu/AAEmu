using System.Text;

namespace AAEmu.Game.Models.Game.Expeditions;

internal static class ExpeditionTextRules
{
    public const int MaximumNoticeUtf8Bytes = 800;
    public const int MaximumRoleNameUtf8Bytes = 128;

    public static bool IsValidNotice(string value) =>
        value != null && Encoding.UTF8.GetByteCount(value) <= MaximumNoticeUtf8Bytes;

    public static bool IsValidRoleName(string value) =>
        value != null && Encoding.UTF8.GetByteCount(value) <= MaximumRoleNameUtf8Bytes;

    public static string TruncateUtf8(string value, int maximumBytes)
    {
        if (string.IsNullOrEmpty(value) || maximumBytes <= 0)
            return string.Empty;
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            return value;

        var builder = new StringBuilder(value.Length);
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var bytes = rune.Utf8SequenceLength;
            if (used + bytes > maximumBytes)
                break;
            builder.Append(rune);
            used += bytes;
        }
        return builder.ToString();
    }
}

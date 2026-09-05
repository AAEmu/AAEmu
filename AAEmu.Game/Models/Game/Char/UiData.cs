using System.Text;

namespace AAEmu.Game.Models.Game.Char;

public static class UiData
{
    public const int MaximumBytes = 8191;

    private static readonly UTF8Encoding Encoding = new(false, true);

    public static bool IsSupported(ushort type) => type is >= 1 and <= 7 or 20;

    public static bool TryDecode(byte[] bytes, out string value)
    {
        value = null;
        if (bytes.Length > MaximumBytes || bytes.Contains((byte)0))
            return false;

        try
        {
            value = Encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public static bool TryEncode(string value, out byte[] bytes)
    {
        bytes = [];
        if (value == null || value.Length > MaximumBytes || value.Contains('\0'))
            return false;

        try
        {
            if (Encoding.GetByteCount(value) > MaximumBytes)
                return false;
            bytes = Encoding.GetBytes(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}

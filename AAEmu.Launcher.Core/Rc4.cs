namespace AAEmu.Launcher.Core;

/// <summary>
/// RC4 stream cipher for encrypting the authentication ticket.
/// </summary>
internal static class Rc4
{
    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        var s = new byte[256];
        int i, j;
        for (i = 0; i < 256; i++)
            s[i] = (byte)i;

        j = 0;
        for (i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var output = new byte[data.Length];
        i = 0; j = 0;
        for (var n = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return output;
    }
}

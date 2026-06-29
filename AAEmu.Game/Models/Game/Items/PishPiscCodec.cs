using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// CryNetwork "pish/pisc" variable-length uint group codec.
/// Values are packed in groups of up to 4: a single "pish" header byte holds 2 bits per value giving its
/// little-endian byte length (1..4), immediately followed by the "pisc" bytes for that group. The same codec
/// encodes the equipment gem block and the character appearance customization arrays.
/// </summary>
public static class PishPiscCodec
{
    /// <summary>Writes <paramref name="values"/> as consecutive pish/pisc groups of up to 4.</summary>
    public static void Write(PacketStream stream, IReadOnlyList<uint> values)
    {
        for (var i = 0; i < values.Count; i += 4)
        {
            var groupCount = Math.Min(4, values.Count - i);
            byte pish = 0;
            var pisc = new List<byte>(16);
            for (var j = 0; j < groupCount; j++)
            {
                var v = values[i + j];
                var length = v < 0x100u ? 1 : v < 0x10000u ? 2 : v < 0x1000000u ? 3 : 4;
                pish |= (byte)((length - 1) << (2 * j));
                for (var b = 0; b < length; b++)
                    pisc.Add((byte)(v >> (8 * b)));
            }
            stream.Write(pish);
            foreach (var b in pisc)
                stream.Write(b);
        }
    }

    /// <summary>Reads exactly <paramref name="count"/> uint values written by <see cref="Write"/>.</summary>
    public static uint[] Read(PacketStream stream, int count)
    {
        var result = new uint[count];
        var read = 0;
        while (read < count)
        {
            var groupCount = Math.Min(4, count - read);
            var pish = stream.ReadByte();
            for (var j = 0; j < groupCount; j++)
            {
                var length = ((pish >> (2 * j)) & 3) + 1;
                uint v = 0;
                for (var b = 0; b < length; b++)
                    v |= (uint)stream.ReadByte() << (8 * b);
                result[read++] = v;
            }
        }
        return result;
    }
}

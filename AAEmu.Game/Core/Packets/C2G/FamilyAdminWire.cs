using System.IO;
using System.Text;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Wire guards shared by the family administration requests.
/// </summary>
/// <remarks>
/// A family notice and a family name are the two client-authored strings that overwrite
/// persisted family state. Both arrive as a length-prefixed UTF-8 string, so a body whose
/// declared length does not fit the packet, or whose declared length exceeds the documented
/// read limit, must be refused at the wire. The generic string reader answers a short read with
/// an empty string, which is a legitimate value for a notice, so the shortfall has to be
/// detected before the value reaches the family aggregate.
/// </remarks>
internal static class FamilyAdminWire
{
    /// <summary>
    /// Reads one length-prefixed UTF-8 string, refusing an over-limit or truncated body.
    /// </summary>
    /// <param name="stream">The packet body being read.</param>
    /// <param name="packetName">Name used in the thrown message.</param>
    /// <param name="maximumUtf8Bytes">Largest payload the reader accepts, in UTF-8 bytes.</param>
    /// <returns>The decoded string, or null when the body was refused.</returns>
    public static string ReadBoundedString(PacketStream stream, string packetName, int maximumUtf8Bytes)
    {
        if (stream.Count - stream.Pos < sizeof(short))
            throw new InvalidDataException(
                $"{packetName}: body is {stream.Count - stream.Pos} byte(s); expected a string length prefix.");

        // The prefix is read here rather than by the generic string reader so that an over-limit or
        // short body is refused before any payload byte is consumed.
        var declared = stream.ReadInt16();
        if (declared < 0 || declared > maximumUtf8Bytes)
            throw new InvalidDataException(
                $"{packetName}: declared string length {declared} is outside 0..{maximumUtf8Bytes} bytes.");

        if (stream.Count - stream.Pos < declared)
            throw new TruncatedPacketException(
                $"{packetName}: declared string length {declared} exceeds the {stream.Count - stream.Pos} byte(s) that remain.");

        var value = declared == 0 ? string.Empty : Encoding.UTF8.GetString(stream.ReadBytes(declared));
        if (stream.Overran)
            throw new TruncatedPacketException($"{packetName}: read past the end of the body.");

        return value;
    }
}

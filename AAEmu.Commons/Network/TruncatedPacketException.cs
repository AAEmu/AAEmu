using System.IO;

namespace AAEmu.Commons.Network;

/// <summary>
/// A read ran past the end of a <see cref="PacketStream"/> that was armed with
/// <see cref="PacketStream.StrictReads"/>. The stream already recorded the condition in
/// <see cref="PacketStream.Overran"/> and logged it; this type carries the fact that the read
/// stopped there rather than answering a default value.
/// </summary>
/// <remarks>
/// It sits under <see cref="IOException"/>, not under <see cref="InvalidDataException"/>, because
/// the framework seals that type. A malformed-body guard in this codebase throws
/// <c>new InvalidDataException(...)</c> and <see cref="IOException"/> is its base, so a handler
/// written against either name still recognises a truncated body. It is a distinct type because a
/// client that simply cut a frame short is expected traffic on a public connection rather than a
/// server defect: the packet decode path downgrades this one case to a warning, while a genuine
/// handler bug still logs as fatal.
/// </remarks>
public sealed class TruncatedPacketException : IOException
{
    public TruncatedPacketException(string message) : base(message)
    {
    }
}

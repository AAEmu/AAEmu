using AAEmu.Commons.Network;
using NLog;

namespace AAEmu.Game.Models.Game.SailingActivity;

/// <summary>
/// The element container used by the sailing-activity packets that carry a list
/// (<c>CS 0x214</c>, <c>SC 0x38D</c>, <c>SC 0x38E</c> and the two packets whose opcodes are
/// still unresolved).
/// </summary>
/// <remarks>
/// <para>
/// The 10.0.2.13 serializer for this slot is a generic vector helper whose element type is never
/// named, so neither the element layout nor the element width is known. Nothing here decodes it.
/// </para>
/// <para>
/// Consequently there is deliberately <b>no</b> API that builds a container out of decoded
/// elements: a server cannot author elements it cannot describe, and inventing a layout would
/// put fabricated bytes on the wire. The only way to obtain a non-empty container is
/// <see cref="FromRaw"/>, which round-trips bytes that already exist. <see cref="Write"/> emits
/// those bytes verbatim, which is what makes a byte-exact encode test possible without claiming
/// knowledge the evidence does not support.
/// </para>
/// <para>
/// <b>Scope warning.</b> The same helper is not exclusive to this feature: seven packets call it in
/// total, the five above plus <c>SC 0x393 SystemFeatureStateList</c> (two containers) and
/// <c>SC 0x394 SystemFeatureStateChanged</c> (one). The class is named for sailing because sailing
/// is what this slice implemented, not because the slot is sailing's.
/// </para>
/// <para>
/// <see cref="ReadRemainder"/> consumes the rest of the stream and reports the refusal. That is
/// sound only because the container is the last thing on the wire: once the leading fields are
/// read, every remaining byte belongs to the container, so the parser cannot desynchronise. In all
/// seven packets that hold today it is the only element, or the last of several. Before this class
/// is reused for the 0x39x pair, re-derive that for those packets specifically — a packet that puts
/// a container in the middle of a body would need a length prefix, and there is none to read.
/// </para>
/// </remarks>
public sealed class SailingActivityContainer
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Why the element layout is not decoded, in one sentence.</summary>
    public const string ElementLayoutUnresolved =
        "sailing-activity container element layout is unrecovered; bytes are carried verbatim and never interpreted";

    /// <summary>A container with no elements.</summary>
    public static SailingActivityContainer Empty { get; } = new([]);

    private SailingActivityContainer(byte[] raw)
    {
        Raw = raw;
    }

    /// <summary>The container's bytes exactly as they appear on the wire.</summary>
    public byte[] Raw { get; }

    /// <summary>True when the container carries no bytes at all.</summary>
    public bool IsEmpty => Raw.Length == 0;

    /// <summary>
    /// Always false. Present so a caller can branch on it and fail loudly rather than assume the
    /// elements were understood.
    /// </summary>
    public bool IsDecoded => false;

    /// <summary>Wraps bytes that already exist. The caller is responsible for their content.</summary>
    public static SailingActivityContainer FromRaw(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw.Length == 0 ? Empty : new SailingActivityContainer([.. raw]);
    }

    /// <summary>
    /// Takes every remaining byte of the stream as the container and reports that its elements
    /// were not decoded.
    /// </summary>
    /// <param name="stream">Stream positioned just after the container's leading fields.</param>
    /// <param name="context">Packet name, used only in the diagnostic.</param>
    public static SailingActivityContainer ReadRemainder(PacketStream stream, string context)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.Overran)
        {
            Logger.Error("Sailing activity {0}: stream already overran before its container; no bytes consumed", context);
            return Empty;
        }

        var length = stream.LeftBytes;
        if (length <= 0)
            return Empty;

        var raw = new byte[length];
        Array.Copy(stream.Buffer, stream.Pos, raw, 0, length);
        stream.Pos += length;

        Logger.Warn("Sailing activity {0}: kept {1} container byte(s) verbatim - {2}", context, length, ElementLayoutUnresolved);
        return new SailingActivityContainer(raw);
    }

    /// <summary>Writes the carried bytes verbatim.</summary>
    public void Write(PacketStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (Raw.Length > 0)
            stream.Write(Raw);
    }
}

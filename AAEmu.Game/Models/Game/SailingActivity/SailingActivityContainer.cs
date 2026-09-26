using AAEmu.Commons.Network;
using NLog;

namespace AAEmu.Game.Models.Game.SailingActivity;

/// <summary>
/// A counted vector of signed 32-bit ids, as the client\'s vector helper serializes it.
/// </summary>
/// <remarks>
/// <para>
/// The 10.0.2.13 serializer for this slot is the generic helper the generic vector helper. It writes a
/// 32-bit element count and then that many 32-bit elements, so an <b>empty</b> vector is four zero
/// bytes on the wire, not zero bytes.
/// </para>
/// <para>
/// That cost is arithmetic-checkable against the raw serializer schema, and it is exactly what the
/// shipped bodies require: <c>SC 0x38D</c> calls the helper <b>once</b> (an omitted count makes it
/// 4 bytes short) and <c>SC 0x38E</c> calls it <b>three times</b> (12 bytes short).
/// </para>
/// <para>
/// The element width is corroborated independently by <c>SC 0x391</c>\'s own struct, whose recovered
/// member offsets place a 32-bit count at <c>+0x14</c>, 32 four-byte ids from <c>+0x18</c>, and the
/// first time field at <c>+0x98</c>. <c>0x18 + 32*4 == 0x98</c> exactly.
/// </para>
/// <para>
/// <b>Element type is inferred, not proven.</b> The helper is opaque in the extraction: the schema
/// records the call, not the callee\'s body. That the element is a 32-bit int follows from the offset
/// arithmetic above and from the field names the callers use (<c>rewardIds</c>, <c>stageIds</c>,
/// <c>claimedRewardIds</c>), not from a recovered body for the helper itself.
/// </para>
/// <para>
/// <b>Scope warning.</b> The same helper is not exclusive to this feature: seven packets call it in
/// total, the five sailing ones plus <c>SC 0x393 SystemFeatureStateList</c> (two containers) and
/// <c>SC 0x394 SystemFeatureStateChanged</c> (one). The class is named for sailing because sailing
/// is what this slice implemented, not because the slot is sailing\'s.
/// </para>
/// <para>
/// Because the count is explicit this container no longer has to be the last thing in a body. The
/// earlier remainder-consuming design needed that; a counted vector does not.
/// </para>
/// </remarks>
public sealed class SailingActivityContainer
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>A container with no elements. Encodes as four zero bytes, not as nothing.</summary>
    public static SailingActivityContainer Empty { get; } = new([]);

    private SailingActivityContainer(int[] ids)
    {
        Ids = ids;
    }

    /// <summary>The 32-bit ids this vector carries.</summary>
    public int[] Ids { get; }

    /// <summary>True when the container carries no elements.</summary>
    public bool IsEmpty => Ids.Length == 0;

    /// <summary>Builds a container from ids. The count is written on the wire, not stored here.</summary>
    public static SailingActivityContainer FromIds(params int[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Length == 0 ? Empty : new SailingActivityContainer([.. ids]);
    }

    /// <summary>
    /// Reads a counted vector: a 32-bit count followed by that many 32-bit elements.
    /// </summary>
    /// <param name="stream">Stream positioned at the count.</param>
    /// <param name="context">Packet name, used only in the diagnostic.</param>
    public static SailingActivityContainer Read(PacketStream stream, string context)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var count = stream.ReadInt32();
        if (count < 0)
        {
            Logger.Error("Sailing activity {0}: container declared a negative element count ({1}); refusing", context, count);
            throw new InvalidDataException($"sailing-activity container count must not be negative, got {count}");
        }

        var ids = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (stream.LeftBytes < sizeof(int))
            {
                Logger.Error("Sailing activity {0}: container declared {1} elements but the stream ended after {2}", context, count, i);
                throw new InvalidDataException($"sailing-activity container truncated at element {i} of {count}");
            }

            ids[i] = stream.ReadInt32();
        }

        return count == 0 ? Empty : new SailingActivityContainer(ids);
    }

    /// <summary>Writes the element count and then each id.</summary>
    public void Write(PacketStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(Ids.Length);
        foreach (var id in Ids)
            stream.Write(id);
    }
}

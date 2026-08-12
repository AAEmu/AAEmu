using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Outcome of one awakening ("각성") attempt, opcode 0xD4.
/// </summary>
/// <remarks>
/// <para>Schema:</para>
/// <code>
/// item  before      // the item as it was
/// item  after       // the item as it is now; identical to before on a failure
/// u32   mappingId   // the item_change_mappings row this attempt used
/// u8    result      // 0 = success, non-zero = failure
/// </code>
/// <para>
/// Both item bodies are the ordinary full item body, byte for byte what
/// <see cref="Item.Write(PacketStream)"/> produces, so a change to that serializer changes this packet.
/// </para>
/// <para>
/// Send this on <b>every</b> attempt, success or failure. It is what closes the request: without it the
/// confirm control stays disabled and the player cannot try again. <c>result</c> selects which outcome
/// is presented, so a failure reported as 0 shows a success.
/// </para>
/// </remarks>
public class SCItemChangeMappingResultPacket : GamePacket
{
    private readonly byte[] _before;
    private readonly Item _after;
    private readonly uint _mappingId;
    private readonly byte _result;

    /// <param name="before">
    /// The source item serialized before it was changed. Taken as bytes because the awakening edits
    /// the item in place, so by send time the live object is already the "after" state.
    /// </param>
    public SCItemChangeMappingResultPacket(byte[] before, Item after, uint mappingId, byte result)
        : base(SCOffsets.SCItemChangeMappingResultPacket, 1)
    {
        _before = before;
        _after = after;
        _mappingId = mappingId;
        _result = result;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_before, false);
        _after.Write(stream);
        stream.Write(_mappingId);
        stream.Write(_result);
        return stream;
    }
}

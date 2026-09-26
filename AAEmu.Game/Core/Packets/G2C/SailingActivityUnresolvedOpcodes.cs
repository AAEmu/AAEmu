namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcodes the 10.0.2.13 client defines for the sailing activity but no extraction recovered.
/// </summary>
/// <remarks>
/// <para>
/// Two sailing-activity packets exist in the client's type table, and both have an <c>opcode</c>
/// of <c>null</c> in the raw serializer schema. Their bodies are recovered — a signed 32-bit
/// <c>activityId</c> followed by a number of element containers — but the slot each one occupies
/// is not.
/// </para>
/// <para>
/// The known sailing-activity slots run <c>0x38D</c> (stage unlocked) through <c>0x392</c> (points
/// changed), which leaves three gaps of exactly one slot each. Filling those gaps by counting
/// would be arithmetic on adjacency, not an extraction: it assumes the sequence is contiguous,
/// that no packet was inserted between them, and that the two unresolved bodies sit in the order
/// their vtables happen to be laid out. Any of those three assumptions could be wrong, and a wrong
/// guess is an opcode sent to a real client.
/// </para>
/// <para>
/// So the names are recorded as explicitly unresolved and no packet is registered for them. The
/// same treatment the team-joint slice gave its unknown wire mode. When a live capture or a
/// further extraction pins a slot, the constant is replaced by a real offset here.
/// </para>
/// </remarks>
public static class SailingActivityUnresolvedOpcodes
{
    /// <summary>
    /// <c>SCSailingActivityDataPacket</c>: signed <c>activityId</c> then ten containers. No slot recovered.
    /// </summary>
    public const ushort SailingActivityDataUnresolved = 0;

    /// <summary>
    /// <c>SCSailingActivityTaskProgressPacket</c>: signed <c>activityId</c> then nine containers.
    /// No slot recovered.
    /// </summary>
    public const ushort SailingActivityTaskProgressUnresolved = 0;
}

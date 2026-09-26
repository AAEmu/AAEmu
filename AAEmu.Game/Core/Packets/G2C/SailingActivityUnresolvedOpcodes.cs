namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Sailing-activity slots the 10.0.2.13 client assigns, but whose bodies this slice does not yet send.
/// </summary>
/// <remarks>
/// <para>
/// An earlier revision of this file claimed both of these packets carried <c>opcode = null</c> in the
/// raw serializer schema, and inferred their slots by counting gaps between the known ones. <b>That
/// premise was wrong.</b> The raw schema assigns both explicitly:
/// </para>
/// <list type="bullet">
/// <item><description><c>SCSailingActivityDataPacket</c> - opcode <c>0x38B</c>, an s32 <c>activityId</c> then <b>10</b> containers.</description></item>
/// <item><description><c>SCSailingActivityTaskProgressPacket</c> - opcode <c>0x38C</c>, an s32 <c>activityId</c> then <b>9</b> containers.</description></item>
/// </list>
/// <para>
/// The known slots run <c>0x38B</c> through <c>0x392</c> with no gaps at all, so there was never a gap to
/// count. The opcodes are constants here, not inferences.
/// </para>
/// <para>
/// <b>Why the packets are still not sent.</b> Each container call is recorded by the extraction with no field name, so the ten and nine vectors have no recovered meaning.
/// They are a counted <c>vector&lt;int&gt;</c>, but <em>which</em> ids is unknown - a row of ten
/// anonymous vectors is not something this slice should put on the wire. Naming them needs a capture
/// or a further extraction of the helper's callers; the opcodes below are ready for it.
/// </para>
/// <para>
/// Contrast the two packets that <b>are</b> sent, <c>0x38D</c> and <c>0x38E</c>: their containers
/// belong to callers that name them (<c>rewardIds</c>, <c>stageIds</c>, <c>claimedRewardIds</c>), which is
/// what makes those safe to serialize.
/// </para>
/// </remarks>
public static class SailingActivityUnresolvedOpcodes
{
    /// <summary>
    /// <c>SCSailingActivityDataPacket</c>: opcode <c>0x38B</c>, signed <c>activityId</c> then ten
    /// unnamed containers. Slot recovered; container meanings not.
    /// </summary>
    public const ushort SailingActivityDataPacket = 0x38B;

    /// <summary>
    /// <c>SCSailingActivityTaskProgressPacket</c>: opcode <c>0x38C</c>, signed <c>activityId</c> then
    /// nine unnamed containers. Slot recovered; container meanings not.
    /// </summary>
    public const ushort SailingActivityTaskProgressPacket = 0x38C;
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The bulk clear request (CS 0x163). <b>Parsed and deliberately not acted on.</b>
/// </summary>
/// <remarks>
/// <para>
/// The request is <b>logged as a no-op and mutates nothing</b>. Three independent sources agree that
/// no gameplay path reaches it, and acting on it would be destructive:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The raw <c>ir</c> for <c>CSRemoveCommonFarmsPacket</c> has <b>zero body rows and no handler</b> in
/// both the retail and the development build. Nothing in the retail server reads this opcode.
/// </description></item>
/// <item><description>
/// No file in the 7,737-file client Lua extract sends opcode <c>0x163</c>, by literal or by any
/// farm-packet name.
/// </description></item>
/// <item><description>
/// It is one of the farm <i>shape</i> editor commands. A shape editor request is not a player
/// request, and a player never sends it.
/// </description></item>
/// </list>
/// <para>
/// An earlier version of this handler read it as "drop every farm doodad this character has
/// planted" and did exactly that, deleting the caller's crops — rows included — and answering
/// nothing. That reading was invented here rather than recovered, and a packet that silently
/// destroys a player's world on arrival is not a behaviour to ship on the strength of a plausible
/// name. The body is still parsed so that an unexpected payload is visible in the log rather than
/// silently absorbed.
/// </para>
/// </remarks>
public class CSRemoveCommonFarmsPacket() : GamePacket(CSOffsets.CSRemoveCommonFarmsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        Logger.Debug("RemoveCommonFarms ({0}) from {1}: parsed and ignored. This opcode has no handler "
                     + "in the retail server, no 10.x client sender, and is a farm-shape editor command; "
                     + "it is not a request to clear planted crops.",
            0x163, character?.Name ?? "<no character>");
    }
}

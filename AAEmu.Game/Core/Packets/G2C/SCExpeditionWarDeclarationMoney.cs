using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// 2026-09-02: server's answer to CSRequestDeclarationMoneyPacket. Fires the client UI event
/// EXPEDITION_WAR_DECLARATION_MONEY (native id 0x1d0) -> ShowDeclarationExpeditionWarDialog(unitId,
/// name, money), which opens the "declare war on &lt;guild&gt; for &lt;money&gt;?" confirm dialog. Clicking OK
/// then sends CSDeclareExpeditionWarPacket(Bc = unitId, Money = money).
/// </summary>
/// <remarks>
/// Opcode 0x19: from the real client's inbound dispatcher FUN_39410ef0
/// (handler = *(registry + 8 + opcode*8)); this packet's functor sits at registry+0xd0 -> 0x19.
/// Body from the client Unpack FUN_39aa7350: unitId via the packed-ref reader (Bc), then a u32 "money".
/// The dialog's guild name is resolved client-side from unitId; it is NOT sent here.
/// </remarks>
public class SCExpeditionWarDeclarationMoney(uint bc, uint money) : GamePacket(SCOffsets.SCExpeditionWarDeclarationMoney, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(money);
        return stream;
    }
}

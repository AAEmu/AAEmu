using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// 2026-09-02: first step of the guild "declare war" round trip. The right-click menu on an enemy
/// guild member does NOT send CSDeclareExpeditionWarPacket directly - it first calls
/// X2Faction:RequestDeclarationMoney(), which sends this packet. The server must answer with
/// SCExpeditionWarDeclarationMoney (target unitId + computed cost); only that response opens the
/// confirm dialog, and only clicking OK there sends CSDeclareExpeditionWarPacket.
/// </summary>
/// <remarks>
/// Opcode 0x11, confirmed via Ghidra literal in the real client (FUN_395be140). Body is a single
/// packed reference (Bc) - the target unit - written with the client's 1-arg packed-ref writer
/// FUN_39a8f270 (CSDeclareExpeditionWarPacket by contrast uses the 2-arg Bc+u32 writer). No money
/// field: the server computes and returns the cost.
/// </remarks>
public class CSRequestDeclarationMoneyPacket() : GamePacket(CSOffsets.CSRequestDeclarationMoneyPacket, 1)
{
    public uint Bc { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();

        ExpeditionManager.Instance.RequestDeclarationMoney(Connection, Bc);
    }
}

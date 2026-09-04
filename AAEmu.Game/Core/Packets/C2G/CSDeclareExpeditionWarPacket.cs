using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// 2026-09-02: was a fully-parsed no-op stub - see ExpeditionManager.DeclareWar's doc comment for the fix.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSDeclareExpeditionWarPacket() : GamePacket(CSOffsets.CSDeclareExpeditionWarPacket, 1)
{
    public uint Bc { get; private set; }
    public uint Money { get; private set; }

    public override void Read(PacketStream stream)
    {
        Bc = stream.ReadBc();
        Money = stream.ReadUInt32();

        ExpeditionManager.Instance.DeclareWar(Connection, Bc, Money);
    }
}

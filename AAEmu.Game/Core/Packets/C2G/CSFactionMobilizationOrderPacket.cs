using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// One player's answer to a mobilization order's summon popup.
/// </summary>
/// <remarks>
/// Sent by X2Faction:RequestMobilizationOrder(result, heroId, zoneGroupType) from every exit the popup
/// has - the Accept button, the No button, and the timer running out - so an answer always arrives, even
/// when it is a refusal (x2ui/mobilizationorder/mobilization_order.lua:29-45).
///
/// Result is MOBILIZATION_ORDER_RESULT from that file: 1 accept, 2 cancel, 3 time over.
/// </remarks>
public class CSFactionMobilizationOrderPacket() : GamePacket(CSOffsets.CSFactionMobilizationOrderPacket, 1)
{
    public uint Result { get; private set; }
    public ulong HeroId { get; private set; }
    public short ZoneGroupType { get; private set; }

    public override void Read(PacketStream stream)
    {
        Result = stream.ReadUInt32();
        HeroId = stream.ReadUInt64();
        ZoneGroupType = stream.ReadInt16();
    }

    public override void Execute()
    {
        MobilizationOrderManager.Instance.Answer(Connection?.ActiveChar, Result, HeroId, ZoneGroupType);
    }
}

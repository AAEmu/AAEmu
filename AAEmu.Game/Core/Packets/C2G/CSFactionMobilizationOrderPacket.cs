using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A member's answer to the Mobilization Order popup: accept, cancel, or the popup timing out. The zone
/// group is echoed from the order broadcast and is not acted on.
/// </summary>
public class CSFactionMobilizationOrderPacket() : GamePacket(CSOffsets.CSFactionMobilizationOrderPacket, 1)
{
    public uint Result { get; private set; }
    public ulong HeroId { get; private set; }
    public ushort ZoneGroupType { get; private set; }

    public override void Read(PacketStream stream)
    {
        Result = stream.ReadUInt32();
        HeroId = stream.ReadUInt64();
        ZoneGroupType = stream.ReadUInt16();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (Result == (uint)MobilizationOrderResultType.Accept)
            HeroManager.Instance.AcceptMobilizationOrder(character, HeroId);

        HeroManager.Instance.SendMobilizationOrderCount(character, MobilizationOrderAction.None);
    }
}

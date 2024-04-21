using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestSpecialtyCurrentPacket : GamePacket
{
    public CSRequestSpecialtyCurrentPacket() : base(CSOffsets.CSRequestSpecialtyCurrentPacket, 5)
    {
        //
    }

    public override void Read(PacketStream stream)
    {
        var fromZoneGroupId = stream.ReadUInt16();
        var toZoneGroupId = stream.ReadUInt16();
        Logger.Trace($"RequestSpecialityRates: Player {Connection.ActiveChar.Name}, FromZoneGroup {fromZoneGroupId}, ToZoneGroup {toZoneGroupId}");
        var items = SpecialtyManager.Instance.GetRatiosForTargetRoute(fromZoneGroupId, toZoneGroupId);
        /*
        foreach (var (itemId, rate) in items)
            Connection.ActiveChar.SendMessage($"@ITEM_NAME({itemId}) => {rate}%");
        */
        Connection.ActiveChar.SendPacket(new SCSpecialtyCurrentPacket(fromZoneGroupId, toZoneGroupId, items));
    }
}

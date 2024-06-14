using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestCommonFarmListPacket : GamePacket
{

    public CSRequestCommonFarmListPacket() : base(CSOffsets.CSRequestCommonFarmListPacket, 1)
    {

    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSRequestCommonFarmList");

        Connection.ActiveChar.SendPacket(new SCResponseCommonFarmListPacket(PublicFarmManager.Instance.GetCommonFarmDoodads(Connection.ActiveChar)));
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestTodayAssignmentPacket() : GamePacket(CSOffsets.CSRequestTodayAssignmentPacket, 1)
{
    public uint RealStep { get; private set; }
    public sbyte Request { get; private set; }

    public override void Read(PacketStream stream)
    {
        RealStep = stream.ReadUInt32();
        Request = stream.ReadSByte();
    }

    public override void Execute()
    {
        TodayAssignmentManager.Instance.HandleRequest(Connection.ActiveChar, RealStep, Request);
    }
}

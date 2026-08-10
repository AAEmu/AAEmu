using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTodayAssignmentAcceptAllPacket() : GamePacket(CSOffsets.CSTodayAssignmentAcceptAllPacket, 1)
{
    public sbyte TodayType { get; private set; }
    public uint Count { get; private set; }
    public List<uint> RealSteps { get; } = [];

    public override void Read(PacketStream stream)
    {
        TodayType = stream.ReadSByte();
        Count = stream.ReadUInt32();
        RealSteps.Clear();
        for (var i = 0; i < Count && i < 64; i++)
            RealSteps.Add(stream.ReadUInt32());
    }

    public override void Execute()
    {
        TodayAssignmentManager.Instance.HandleAcceptAll(Connection.ActiveChar, TodayType, RealSteps);
    }
}

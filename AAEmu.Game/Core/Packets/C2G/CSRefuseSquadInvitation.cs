using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRefuseSquadInvitation() : GamePacket(CSOffsets.CSRefuseSquadInvitation, 1)
{
    public int SquadId { get; private set; }
    public int InviationId { get; private set; }
    public long WorldCharKey { get; private set; }
    public sbyte RefuseType { get; private set; }

    public override void Read(PacketStream stream)
    {
        SquadId = stream.ReadInt32();
        InviationId = stream.ReadInt32();
        WorldCharKey = stream.ReadInt64();
        RefuseType = stream.ReadSByte();
        SquadManager.Instance.RefuseInvite(Connection.ActiveChar, SquadId, InviationId, WorldCharKey, RefuseType);
    }
}

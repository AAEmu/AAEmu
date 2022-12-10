using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUseTeleportPacket : GamePacket
    {
        public CSUseTeleportPacket() : base(CSOffsets.CSUseTeleportPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var returnPointId = stream.ReadUInt32();
            var moneyAmount = stream.ReadInt32();
            var npcUnitId = stream.ReadBc();
            
            _log.Warn("CSUseTeleport, ReturnPointId: {0}, MoneyAmount: {1}, NpcUnitId: {2}", returnPointId, moneyAmount, npcUnitId);
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChallengeDuelPacket() : GamePacket(CSOffsets.CSChallengeDuelPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Client layout (VA 0x39C673E0): duelType u8, then the challenged id as u64. We read a bare
        // u32 and no duelType, so the id came out of the wrong bytes entirely.
        var duelType = stream.ReadByte();       // u8  duelType
        var challengedId = stream.ReadUInt64(); // u64 type - who we challenged

        DuelManager.Instance.DuelRequest(Connection.ActiveChar, (uint)challengedId, duelType);
    }
}

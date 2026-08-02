using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCheckRaceCongestionPacket() : GamePacket(CSOffsets.CSCheckRaceCongestionPacket, 1)
{
    // no presence byte on this wire path, so the body is exactly eight bytes.
    public override void Read(PacketStream stream)
    {
        // PacketStream retains the decrypted crc/count/opcode prefix. GameProtocolHandler has already
        // advanced Pos past those four bytes, so validate the unread native body rather than total Count.
        if (stream.LeftBytes != sizeof(ulong))
        {
            Logger.Warn(
                "Rejected malformed race-congestion request from account {0}: body length {1}.",
                Connection.AccountId,
                stream.LeftBytes);
            Connection.SendPacket(new SCCheckRaceCongestionResponsePacket(false));
            return;
        }

        var characterId = stream.ReadUInt64();
        var canEnter = characterId <= uint.MaxValue &&
                       Connection.Characters.ContainsKey((uint)characterId);
        if (!canEnter)
        {
            Logger.Warn(
                "Rejected race-congestion request for character {0} from account {1}.",
                characterId,
                Connection.AccountId);
        }

        Connection.SendPacket(new SCCheckRaceCongestionResponsePacket(canEnter));
    }
}

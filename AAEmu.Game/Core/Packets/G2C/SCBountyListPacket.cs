using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crime;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBountyListPacket(int total, byte count, List<BountyDescription> bounties) : GamePacket(SCOffsets.SCBountyListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(total);
        stream.Write(count);
        var c = 0;
        foreach (var bounty in bounties)
        {
            c++;
            if (c > count)
                break;
            stream.Write(bounty.PlayerId);
            stream.Write(bounty.PlayerName);
            stream.Write(bounty.PlayerLevel);
            stream.Write(bounty.ZoneId);
            stream.Write(Helpers.ConvertLongX(bounty.Pos.X));
            stream.Write(Helpers.ConvertLongX(bounty.Pos.Y));
            stream.Write(bounty.Pos.Z);
            stream.Write(bounty.BountyAmount);
            stream.Write(bounty.CrimePoints);
            stream.Write(bounty.IsLive);
            stream.Write((byte)bounty.PlayerRace);
            stream.Write(bounty.Type184);
            stream.Write(bounty.ArrestCount);
            stream.Write(bounty.AcceptGuiltyCount);
            stream.Write(bounty.AcceptTrialCount);
            stream.Write(bounty.GuiltyCount);
            stream.Write(bounty.NotGuiltyCount);
            stream.Write(bounty.Seconds);
        }
        return stream;
    }
}

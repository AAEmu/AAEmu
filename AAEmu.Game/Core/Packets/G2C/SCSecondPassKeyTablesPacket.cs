using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The scrambled key tables a client draws its second password window from. Answered to a key table
/// request, echoing the request's purpose so the client knows which window it is for.
/// </summary>
public class SCSecondPassKeyTablesPacket : GamePacket
{
    private readonly byte _pktm;
    private readonly uint _time;
    private readonly string[] _keys;

    public SCSecondPassKeyTablesPacket(byte pktm, uint time, string[] keys)
        : base(SCOffsets.SCSecondPassKeyTablesPacket, 1)
    {
        _pktm = pktm;
        _time = time;
        _keys = keys ?? [];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pktm);
        stream.Write(_time);

        // The client reads exactly four tables; fewer would leave it reading whatever follows.
        for (var i = 0; i < SecondPasswordKeyTable.TableCount; i++)
            stream.Write(i < _keys.Length ? _keys[i] : string.Empty);

        return stream;
    }
}

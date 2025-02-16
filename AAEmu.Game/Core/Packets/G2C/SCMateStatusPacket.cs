using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMateStatusPacket : GamePacket
{
    private readonly int _skillCount;
    private readonly int _tagCount;
    private readonly int _chargeCount;
    private readonly Mate _mate;

    public SCMateStatusPacket(Mate mate) : base(SCOffsets.SCMateStatusPacket, 5)
    {
        _skillCount = mate.Skills?.Count ?? 0;
        _skillCount = 0;
        _tagCount = 0;
        _chargeCount = 0;
        _mate = mate;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_mate.ObjId); // bc

        #region skill&tag
        stream.Write(_skillCount); // skillCount

        // TODO я считаю, что в итоге требуется выводить все скиллы, тэги и чарджи
        for (var i = 0; i < _skillCount; i++)
        {
            stream.Write(_mate.Skills[i]);     // type
        }

        stream.Write(_tagCount); // tagCount
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u); // type
        }
        stream.Write(_skillCount); // chargeCount
        for (var i = 0; i < _skillCount; i++)
        {
            stream.Write(0u); // type
        }
        #endregion

        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveStatusPacket : GamePacket
{
    private readonly int _skillCount;
    private readonly int _tagCount;
    private readonly int _chargeCount;
    private readonly Slave _slave;

    public SCSlaveStatusPacket(Slave slave) : base(SCOffsets.SCSlaveStatusPacket, 5)
    {
        _skillCount = slave.Skills?.Count ?? 0;
        _tagCount = 0;
        _chargeCount = 0;
        _slave = slave;
    }


    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_slave.ObjId); // bc
        stream.Write(_slave.TlId);    // tl
        stream.Write(_slave.SummoningItem?.Id ?? 0ul); // type

        #region skill&tag
        stream.Write(_skillCount); // skillCount
        
        // TODO я считаю, что в итоге требуется выводить все скиллы, тэги и чарджи
        for (var i = 0; i < _skillCount; i++)
        {
            stream.Write(_slave.Skills[i]);     // type
        }
        stream.Write(_tagCount); // tagCount
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u); // type
        }
        stream.Write(_chargeCount); // chargeCount
        for (var i = 0; i < _skillCount; i++)
        {
            stream.Write(0u); // type
        }
        #endregion

        stream.Write(_slave.Summoner?.Name ?? string.Empty); // creatorName
        stream.Write(_slave.Summoner?.Id ?? 0);              // ownerId
        stream.Write(_slave.SummoningItem == null ? 0 : _slave.Id); // dbId DbHouseId, для связных должен быть 0

        return stream;
    }
}

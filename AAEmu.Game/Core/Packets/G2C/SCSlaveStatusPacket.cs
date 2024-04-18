using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveStatusPacket : GamePacket
{
    private readonly int _skillCount;
    private readonly int _tagCount = 0;
    private readonly Slave _slave;

    public SCSlaveStatusPacket(Slave slave) :
        base(SCOffsets.SCSlaveStatusPacket, 5)
    {
        _slave = slave;
        _skillCount = slave.Skills.Count;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_slave.ObjId); // bc
        stream.Write(_slave.TlId);    // tl
        stream.Write(_slave.SummoningItem?.Id ?? 0ul); // type

        #region skill&tag
        var count = _slave.Skills.Count;
        stream.Write(count); // skillCount
        foreach (var skill in _slave.Skills)
        {
            stream.Write(skill);   // type
        }
        stream.Write(_tagCount); // tagCount
        for (var i = 0; i < _tagCount; i++)
        {
            stream.Write(0u);   // type
            stream.Write(0u);  // type
            stream.Write(0u); // type
        }
        #endregion
        stream.Write(_slave.Summoner.Name ?? string.Empty);  // creatorName
        stream.Write(_slave.Summoner?.ObjId ?? 0);          // type
        stream.Write(_slave.Id);                           // dbId DbHouseId

        return stream;
    }
}

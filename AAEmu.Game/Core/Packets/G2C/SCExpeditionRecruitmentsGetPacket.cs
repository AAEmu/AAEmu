using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionRecruitmentsGetPacket : GamePacket
{
    private readonly uint _total;
    private readonly uint _page;
    private readonly uint _count;
    private sbyte _applyCount;
    private readonly List<ExpeditionRecruitment> _recruitments;

    public SCExpeditionRecruitmentsGetPacket(List<ExpeditionRecruitment> recruitments)
        : base(SCOffsets.SCExpeditionRecruitmentsGetPacket, 5)
    {
        _total = 1;
        _page = 1;
        _count = 1;
        _applyCount = 0;
        _recruitments = recruitments;
    }
    public SCExpeditionRecruitmentsGetPacket(uint total, uint page, uint count, List<ExpeditionRecruitment> recruitments)
        : base(SCOffsets.SCExpeditionRecruitmentsGetPacket, 5)
    {
        _total = total;
        _page = page;
        _count = count;
        _applyCount = 0;
        _recruitments = recruitments;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_page);
        stream.Write(_count);
        foreach (var member in _recruitments) // 15 на страницу
        {
            member.Write(stream);
            if (member.Apply)
            {
                _applyCount++;
            }
        }

        stream.Write(_applyCount);
        return stream;
    }
}

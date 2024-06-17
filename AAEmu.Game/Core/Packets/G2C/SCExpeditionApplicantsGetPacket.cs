using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionApplicantsGetPacket : GamePacket
{
    private readonly int _total;
    private readonly int _count;
    private readonly sbyte _applyCount;
    private readonly List<Applicant> _pretenders;

    public SCExpeditionApplicantsGetPacket(int total, List<Applicant> pretenders) : base(SCOffsets.SCExpeditionApplicantsGetPacket, 5)
    {
        _total = total;
        _count = pretenders.Count;
        _pretenders = pretenders;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_count);
        foreach (var pretender in _pretenders) // 50 на страницу
            pretender.WriteInfo(stream);

        return stream;
    }
}

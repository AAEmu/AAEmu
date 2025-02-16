using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestsPacket : GamePacket
{
    private readonly Quest[] _quests;

    public SCQuestsPacket(Quest[] quests) : base(SCOffsets.SCQuestsPacket, 5)
    {
        _quests = quests;
    }

    public override PacketStream Write(PacketStream stream)
    {
        const int MaxQuests = 20;
        if (_quests.Length > MaxQuests)
        {
            throw new InvalidOperationException($"Number of quests exceeds the maximum allowed ({MaxQuests}).");
        }

        stream.Write(_quests.Length); // count
        foreach (var quest in _quests)
        {
            quest.Write(stream);
        }
        return stream;
    }
}

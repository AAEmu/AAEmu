using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCompletedQuestsPacket : GamePacket
{
    private readonly CompletedQuest[] _quests;

    public SCCompletedQuestsPacket(CompletedQuest[] quests) : base(SCOffsets.SCCompletedQuestsPacket, 5)
    {
        _quests = quests;
    }

    public override PacketStream Write(PacketStream stream)
    {
        const int MaxQuests = 200;
        if (_quests.Length > MaxQuests)
        {
            throw new InvalidOperationException($"Number of quests exceeds the maximum allowed ({MaxQuests}).");
        }

        stream.Write(_quests.Length);
        foreach (var quest in _quests)
        {
            if (quest.Body.Length != 64) // 64 / 8 = 8 байт
            {
                throw new InvalidOperationException("Quest body must be exactly 8 bytes.");
            }

            var body = new byte[8];
            quest.Body.CopyTo(body, 0);

            stream.Write(quest.Id); // idx
            stream.Write(body); // body
        }
        return stream;
    }
}

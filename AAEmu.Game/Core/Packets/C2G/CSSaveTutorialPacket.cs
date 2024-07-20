﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSaveTutorialPacket : GamePacket
{
    public CSSaveTutorialPacket() : base(CSOffsets.CSSaveTutorialPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32();

        Logger.Debug($"SaveTutorial, Id: {id}");

        var completedQuestBlock = Connection.ActiveChar.Quests.SetCompletedQuestFlag(id, true);
        var body = new byte[8];
        completedQuestBlock.Body.CopyTo(body, 0);

        Connection.SendPacket(new SCTutorialSavedPacket(id, body));
    }
}

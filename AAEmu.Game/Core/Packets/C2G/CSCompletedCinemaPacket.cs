﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCompletedCinemaPacket : GamePacket
{
    public CSCompletedCinemaPacket() : base(CSOffsets.CSCompletedCinemaPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Empty struct
        _log.Warn("CompletedCinema");

        WorldManager.ResendVisibleObjectsToCharacter(Connection.ActiveChar);
    }
}

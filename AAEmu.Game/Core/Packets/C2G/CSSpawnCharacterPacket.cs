﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Observers;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpawnCharacterPacket : GamePacket
    {
        public CSSpawnCharacterPacket() : base(CSOffsets.CSSpawnCharacterPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.State = GameState.World;

            Connection.ActiveChar.VisualOptions = new CharacterVisualOptions();
            Connection.ActiveChar.VisualOptions.Read(stream);

            Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));
            Connection.ActiveChar.PushSubscriber(TimeManager.Instance.Subscribe(Connection, new TimeOfDayObserver(Connection.ActiveChar)));

            _log.Info("CSSpawnCharacterPacket");
        }
    }
}

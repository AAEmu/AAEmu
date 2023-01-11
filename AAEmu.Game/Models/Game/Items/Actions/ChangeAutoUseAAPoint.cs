﻿using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ChangeAutoUseAAPoint : ItemTask
    {
        private readonly byte _change;

        public ChangeAutoUseAAPoint(byte change)
        {
            _type = ItemAction.ChangeAutoUseAaPoint; // 18
            _change = change;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_change); // change
            return stream;
        }
    }
}

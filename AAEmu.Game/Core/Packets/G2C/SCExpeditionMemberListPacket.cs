﻿using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpeditionMemberListPacket : GamePacket
    {
        private readonly uint _total;
        private readonly uint _id;
        private readonly List<ExpeditionMember> _members;

        public SCExpeditionMemberListPacket(uint total, uint id, List<ExpeditionMember> members) : base(SCOffsets.SCExpeditionMemberListPacket, 5)
        {
            _total = total;
            _id = id;
            _members = members;
        }

        public SCExpeditionMemberListPacket(Expedition expedition) : base(SCOffsets.SCExpeditionMemberListPacket, 5)
        {
            _total = (uint)expedition.Members.Count;
            _id = expedition.Id;
            _members = expedition.Members; // TODO in 1.2 ... 2.0 max 20
        }

        public override PacketStream Write(PacketStream stream)
        {
            //stream.Write(_total); // missing in version 2.0
            stream.Write((byte)_members.Count); // TODO in 1.2 ... 2.0 max length 20
            stream.Write(_id); // expedition id
            foreach (var member in _members)
            {
                stream.Write(member);
            }

            return stream;
        }
    }
}

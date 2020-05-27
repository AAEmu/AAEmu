﻿using AAEmu.Commons.Network.Core;

 namespace AAEmu.Commons.Network.Share.Character
{
    public class CharacterInfo : PacketMarshaler
    {
        public ulong AccountId { get; set; }
        public byte GameServerId { get; set; }
        public uint CharacterId { get; set; }
        public string Name { get; set; }
        public byte Race { get; set; }
        public byte Gender { get; set; }

        public override void Read(PacketStream stream)
        {
            AccountId = stream.ReadUInt64();
            GameServerId = stream.ReadByte();
            CharacterId = stream.ReadUInt32();
            Name = stream.ReadString();
            Race = stream.ReadByte();
            Gender = stream.ReadByte();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(AccountId);
            stream.Write(GameServerId);
            stream.Write(CharacterId);
            stream.Write(Name);
            stream.Write(Race);
            stream.Write(Gender);
            return stream;
        }
    }
}

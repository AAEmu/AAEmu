﻿using System;
using System.Linq;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAesXorKeyPacket : GamePacket
    {
        public CSAesXorKeyPacket() : base(CSOffsets.CSAesXorKeyPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var len = stream.ReadInt32();    // lenAES?
            var len2 = stream.ReadInt16();  // lenXOR?

            var encAes = stream.ReadBytes(128);
            var encXor = stream.ReadBytes(128);
            EncryptionManager.Instance.StoreClientKeys(encAes, encXor, Connection.AccountId, Connection.Id);

            Connection.SendPacket(new SCGetSlotCountPacket(0));
            Connection.SendPacket(new SCAccountInfoPacket((int)Connection.Payment.Method, Connection.Payment.Location, Connection.Payment.StartTime, Connection.Payment.EndTime));
            Connection.SendPacket(new SCAccountAttendancePacket(31));

            Connection.SendPacket(new SCRaceCongestionPacket());
            Connection.LoadAccount();
            var characters = Connection.Characters.Values.ToArray();

            if (characters.Length == 0)
            {
                Connection.SendPacket(new SCCharacterListPacket(true, characters));
            }
            else
            {
                for (var i = 0; i < characters.Length; i += 2)
                {
                    var last = characters.Length - i <= 2;
                    var temp = new Character[last ? characters.Length - i : 2];
                    Array.Copy(characters, i, temp, 0, temp.Length);
                    Connection.SendPacket(new SCCharacterListPacket(last, temp));
                }
            }
        }
    }
}

using System;
using System.Linq;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAesXorKey_05_Packet : GamePacket
{
    public CSAesXorKey_05_Packet() : base(CSOffsets.CSAesXorKeyPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var len = stream.ReadInt32();    // lenAES?
        var len2 = stream.ReadInt16(); // lenXOR?

        if (len != 0 && len2 != 0)
        {
            var encAes = stream.ReadBytes(len / 2);
            var encXor = stream.ReadBytes(len2 / 2);
            EncryptionManager.Instance.StoreClientKeys(encAes, encXor, Connection.AccountId, Connection.Id);
        }

        Connection.SendPacket(new SCGetSlotCountPacket(0));
        // not needed in 5070
        //Connection.SendPacket(new SCAccountInfoPacket((int)Connection.Payment.Method, Connection.Payment.Location, Connection.Payment.StartTime, Connection.Payment.EndTime));
        // needed in 5070, but I don’t know how to add it here yet
        //Connection.SendPacket(new SCAccountAttendancePacket(31));
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

        //Connection.SendPacket(new SCAccountAttributePacket());
    }
}

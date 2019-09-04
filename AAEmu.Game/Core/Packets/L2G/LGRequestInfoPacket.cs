using System.Collections.Generic;
using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2L;

namespace AAEmu.Game.Core.Packets.L2G
{
    public class LGRequestInfoPacket : LoginPacket
    {
        public LGRequestInfoPacket() : base(0x03)
        {
        }

        public override void Read(PacketStream stream)
        {
            var connectionId = stream.ReadUInt32();
            var requestId = stream.ReadUInt32();
            var accountId = stream.ReadUInt32();
            var characters = accountId != 0
                ? CharacterManager.Instance.LoadCharacters(accountId)
                : new List<LoginCharacterInfo>();
            Connection.SendPacket(new GLRequestInfoPacket(connectionId, requestId, characters));
        }
    }
}

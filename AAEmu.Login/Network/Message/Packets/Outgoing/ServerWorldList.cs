using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;
using AAEmu.Commons.Network.Share.Character;
using AAEmu.Login.Login;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerWorldList)]
    public class ServerWorldList : IWritable
    {
        public IEnumerable<GameServerInstance> GameServers { get; set; }
        public IEnumerable<CharacterInfo> Characters { get; set; }
        
        public void Write(PacketStream stream)
        {
            stream.Write((byte)GameServers.Count());
            foreach (var gameServer in GameServers)
            {
                stream.Write(gameServer);
            }
            
            stream.Write((byte)Characters.Count());
            foreach (var character in Characters)
            {
                stream.Write(character);
                stream.Write(new byte[16], true); //guid
                stream.Write(0L); //v
            }
        }
    }
}

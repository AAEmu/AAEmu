using System;
using System.Collections.Generic;
using System.Net;
using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Network.Connections
{
    public class LoginConnection
    {
        private Session _session;

        public uint Id => _session.Id;
        public IPAddress Ip => _session.Ip;
        public InternalConnection InternalConnection { get; set; }
        public PacketStream LastPacket { get; set; }

        public uint AccountId { get; set; }
        public string AccountName { get; set; }
        public DateTime LastLogin { get; set; }
        public IPAddress LastIp { get; set; }

        public Dictionary<byte, List<LoginCharacterInfo>> Characters;

        public LoginConnection(Session session)
        {
            _session = session;

            Characters = new Dictionary<byte, List<LoginCharacterInfo>>();
        }

        public void SendPacket(LoginPacket packet)
        {
            SendPacket(packet.Encode());
        }

        public void SendPacket(byte[] packet)
        {
            _session?.SendPacket(packet);
        }

        public void OnConnect()
        {
        }

        public void Shutdown()
        {
            _session?.Close();
        }

        public List<LoginCharacterInfo> GetCharacters()
        {
            var res = new List<LoginCharacterInfo>();
            foreach (var characters in Characters.Values)
                if (characters != null)
                    res.AddRange(characters);
            return res;
        }

        public void AddCharacters(byte gsId, List<LoginCharacterInfo> characterInfos)
        {
            foreach (var character in characterInfos)
                character.GsId = gsId;
            Characters.Add(gsId, characterInfos);
        }
    }
}

/*
 * by uranusq https://github.com/NL0bP/aaa_emulator
 * by Nikes
 * by NLObP: метод шифрации для ВСЕХ
 */
using System.Security.Cryptography;

namespace AAEmu.Commons.Cryptography
{
    public class ConnectionKeychain
    {
        public uint ConnectionId { get; set; }
        public RSACryptoServiceProvider RsaKeyPair { get; set; }
        public bool RecievedKeys { get; set; } = false;
        public byte[] AesKey { get; set; }
        public byte[] IV { get; set; }
        public uint XorKey { get; set; }
        public uint XorKeyConstant1 { get; set; }
        public uint XorKeyConstant2 { get; set; }
        public byte SCMessageCount { get; set; }
        public byte CSMessageCount { get; set; }
        public byte CSOffsetSequence { get; set; }
        public uint CSSecondaryOffsetSequence { get; set; }

        public ConnectionKeychain(uint connId, RSACryptoServiceProvider kp)
        {
            ConnectionId = connId;
            RsaKeyPair = kp;
            AesKey = new byte[16];
            IV = new byte[16];
            XorKey = 0;
            XorKeyConstant1 = 0;
            XorKeyConstant2 = 0;
        }
    }
}

/*
 * by uranusq https://github.com/NL0bP/aaa_emulator
 * by Nikes
 * by NLObP: оригинальный метод шифрации (как в crynetwork.dll)

 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

using NLog;

namespace AAEmu.Commons.Cryptography
{
    public class EncryptionManager : Singleton<EncryptionManager>
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private static int DwKeySize { get; } = 1024;
        private Dictionary<ulong, ConnectionKeychain> ConnectionKeys { get; set; } //Dictionary of valid keys bound to account Id and connection Id

        public void Load()
        {
            ConnectionKeys = new Dictionary<ulong, ConnectionKeychain>();

            _log.Info("Loaded Encryption Manager.");
        }

        private ConnectionKeychain GetConnectionKeys(uint connectionId, ulong accountId)
        {
            if (ConnectionKeys.ContainsKey(accountId) && ConnectionKeys[accountId].ConnectionId == connectionId)
            {
                return ConnectionKeys[accountId];
            }
            return GenerateRsaKeyPair(connectionId, accountId);
        }

        private ConnectionKeychain GenerateRsaKeyPair(uint connectionId, ulong accountId)
        {
            if (ConnectionKeys.ContainsKey(accountId))
            {
                ConnectionKeys.Remove(accountId);
            }
            var rsaKeyPair = new RSACryptoServiceProvider(); // по умолчанию DwKeySize равен 1024
            //var pub_key = rsaKeyPair.ExportParameters(false); // export public key
            //var priv_key = rsaKeyPair.ExportParameters(true); // export private key
            var keys = new ConnectionKeychain(connectionId, rsaKeyPair);
            ConnectionKeys.Add(accountId, keys);
            return keys;
        }

        public PacketStream WritePubKey(uint connectionId, ulong accountId, PacketStream stream)
        {
            var keychain = GenerateRsaKeyPair(connectionId, accountId);
            var rsaParameters = keychain.RsaKeyPair.ExportParameters(false);
            stream.Write(rsaParameters.Modulus);
            stream.Write(new byte[125]);
            stream.Write(rsaParameters.Exponent);
            return stream;
        }

        public void StoreClientKeys(byte[] aesKeyEncrypted, byte[] xorKeyEncrypted, ulong accountId, ulong connectionId)
        {
            if (!ConnectionKeys.ContainsKey(accountId))
            {
                return;
            }
            _log.Warn("AccountId: {0}, ConnectionId: {1}", accountId, connectionId);
            var keys = ConnectionKeys[accountId];
            var xorConstRaw = keys.RsaKeyPair.Decrypt(xorKeyEncrypted, false);
            var head = BitConverter.ToUInt32(xorConstRaw, 0);
            _log.Warn("raw XOR: {0}", head); // <-- этот сырой XOR записываем в поле xorConst from AAEMU моего OpcodeFinder`a
            //head = (head ^ 0x15A0244B) * head ^ 0x70F1F23 & 0xffffffff; // 1.2.0.0 AA 18 march 2015
            head = (head ^ 0x15A02491) * head ^ 0x70F1F23 & 0xffffffff; // 1.7.0.0 AA 23 june 2015
            _log.Warn("key XOR: {0}", head); // настоящий ключ XOR
            keys.XorKey = head * head & 0xffffffff;
            keys.AesKey = keys.RsaKeyPair.Decrypt(aesKeyEncrypted, false);
            keys.RecievedKeys = true;
            _log.Warn("AES: {0} XOR: {1}", ByteArrayToString(keys.AesKey), keys.XorKey);
        }

        public byte GetSCMessageCount(uint connectionId, ulong accountId)
        {
            var keys = GetConnectionKeys(connectionId, accountId);
            return keys.SCMessageCount;
        }

        public void IncSCMsgCount(uint connectionId, ulong accountId)
        {
            var keys = GetConnectionKeys(connectionId, accountId);
            keys.SCMessageCount++;
        }

        #region S->C Encryption
        //Methods for SC packet Encryption
        /// <summary>
        /// Подсчет контрольной суммы пакета, используется в шифровании пакетов DD05 и 0005
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <returns>Crc8</returns>
        private byte Crc8(byte[] data, int size)
        {
            var len = size;
            uint checksum = 0;
            for (var i = 0; i <= len - 1; i++)
            {
                checksum *= 0x13;
                checksum += data[i];
            }
            return (byte)(checksum);
        }

        public byte Crc8(byte[] data)
        {
            var size = data.Length;
            return Crc8(data, size);
        }
        //--------------------------------------------------------------------------------------
        /// <summary>
        /// вспомогательная подпрограмма для encode/decode серверных/клиентских пакетов
        /// </summary>
        /// <param name="cry"></param>
        /// <returns></returns>
        private byte Inline(ref uint cry)
        {
            cry += 0x2FCBD5U;
            var n = (byte)(cry >> 0x10);
            n = (byte)(n & 0x0F7);
            return (byte)(n == 0 ? 0x0FE : n);
        }

        //--------------------------------------------------------------------------------------
        /// <summary>
        /// подпрограмма для encode/decode серверных пакетов, правильно шифрует и расшифровывает серверные пакеты DD05 для версии 3.0.3.0
        /// </summary>
        /// <param name="bodyPacket">адрес начиная с байта за DD05</param>
        /// <returns>возвращает адрес на подготовленные данные</returns>
        public byte[] StoCEncrypt(byte[] bodyPacket)
        {
            var length = bodyPacket.Length;
            var array = new byte[length];
            var cry = (uint)(length ^ 0x1F2175A0);
            return ByteXor(bodyPacket, length, array, cry);
        }

        private byte[] ByteXor(byte[] bodyPacket, int length, byte[] array, uint cry, int offset = 0)
        {
            var n = 4 * (length / 4);
            for (var i = n - 1 - offset; i >= 0; i--)
            {
                array[i] = (byte)(bodyPacket[i] ^ (uint)Inline(ref cry));
            }
            for (var i = n - offset; i < length; i++)
            {
                array[i] = (byte)(bodyPacket[i] ^ (uint)Inline(ref cry));
            }
            return array;
        }
        #endregion

        #region C->S Decryption
        //Methods for CS packet Decryption
        //------------------------------
        // здесь распаковка пакетов от клиента 0005
        // для дешифрации следующих пакетов iv = шифрованный предыдущий пакет
        //------------------------------
        public byte[] Decode(byte[] data, uint connectionId, ulong accountId)
        {
            var keys = GetConnectionKeys(connectionId, accountId);
            var iv = keys.IV;
            var xorKey = keys.XorKey;
            var aesKey = keys.AesKey;
            var ciphertext = DecodeXor(data, xorKey, keys);
            var plaintext = DecodeAes(ciphertext, aesKey, iv);
            keys.CSMessageCount++;
            return plaintext;
        }
        //--------------------------------------------------------------------------------------
        /// <summary>
        ///  toClientEncr help function
        /// </summary>
        /// <param name="cry"></param>
        /// <returns></returns>
        private static byte Add(ref uint cry)
        {
            cry += 0x2FCBD5;
            var n = (byte)(cry >> 0x10);
            n = (byte)(n & 0x0F7);
            return (byte)(n == 0 ? 0x0FE : n);
        }
        //--------------------------------------------------------------------------------------
        /// <summary>
        ///  toClientEncr help function
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        private static byte MakeSeq(ConnectionKeychain keys)
        {
            var seq = keys.CSSecondaryOffsetSequence;
            seq += 0x2FA245;
            var result = (byte)(seq >> 0xE & 0x73);
            if (result == 0)
            {
                result = (byte)0xFEu;
            }
            keys.CSSecondaryOffsetSequence = seq;
            return result;
        }

        public static byte[] DecodeXor(byte[] bodyPacket, uint xorKey, ConnectionKeychain keys)
        {
            //          +-Hash начало блока для DecodeXOR, где второе число, в данном случае F(16 байт)-реальная длина данных в пакете, к примеру A(10 байт)-реальная длина данных в пакете
            //          |  +-начало блока для DecodeAES
            //          V  V
            //1300 0005 3F D831012E6DFA489A268BC6AD5BC69263
            var seq = keys.CSOffsetSequence;
            var mBodyPacket = new byte[bodyPacket.Length - 3];
            Buffer.BlockCopy(bodyPacket, 3, mBodyPacket, 0, bodyPacket.Length - 3);
            var msgKey = ((uint)(bodyPacket.Length / 16 - 1) << 4) + (uint)(bodyPacket[2] - 47); // это реальная длина данных в пакете
            var array = new byte[mBodyPacket.Length];
            var mul = msgKey * xorKey; // <-- ставим бряк здесь и смотрим xorKey, packetBody, aesKey, IV для моего OpcodeFinder`a
            //var cry = mul ^ ((uint)MakeSeq(keys) + 0x75A02461) ^ 0xBEB8E892; // 1.2.0.0 AA 18 march 2015
            var cry = mul ^ ((uint)MakeSeq(keys) + 0x75A024A7) ^ 0xE4B868D6; // 1.7.0.0 AA 23 june 2015
            var offset = 4;
            if (seq != 0)
            {
                if (seq % 3 != 0)
                {
                    if (seq % 5 != 0)
                    {
                        if (seq % 7 != 0)
                        {
                            if (seq % 9 != 0)
                            {
                                if (!(seq % 11 != 0)) { offset = 7; }
                            }
                            else { offset = 3; }
                        }
                        else { offset = 11; }
                    }
                    else { offset = 2; }
                }
                else { offset = 5; }
            }
            else { offset = 9; }
            var n = offset * (mBodyPacket.Length / offset);
            for (var i = n - 1; i >= 0; i--)
            {
                array[i] = (byte)(mBodyPacket[i] ^ (uint)Add(ref cry));
            }
            for (var i = n; i < mBodyPacket.Length; i++)
            {
                array[i] = (byte)(mBodyPacket[i] ^ (uint)Add(ref cry));
            }
            keys.CSOffsetSequence += MakeSeq(keys);
            keys.CSOffsetSequence += 1;
            return array;
        }
        //--------------------------------------------------------------------------------------
        private const int Size = 16;
        //--------------------------------------------------------------------------------------
        private static RijndaelManaged CryptAes(byte[] aesKey, byte[] iv)
        {
            var rm = new RijndaelManaged
            {
                KeySize = 128,
                BlockSize = 128,
                Padding = PaddingMode.None,
                Mode = CipherMode.CBC,
                Key = aesKey,
                IV = iv
            };
            return rm;
        }
        //--------------------------------------------------------------------------------------
        /// <summary>
        /// DecodeAes: расшифровка пакета от клиента AES ключом
        /// </summary>
        /// <param name="cipherData"></param>
        /// <param name="aesKey"></param>
        /// <param name="iv"></param>
        /// <returns></returns>
        //--------------------------------------------------------------------------------------
        public static byte[] DecodeAes(byte[] cipherData, byte[] aesKey, byte[] iv)
        {
            var mIv = new byte[16];
            Buffer.BlockCopy(iv, 0, mIv, 0, Size);
            var len = cipherData.Length / Size;
            //Save last 16 bytes in IV
            Buffer.BlockCopy(cipherData, (len - 1) * Size, iv, 0, Size);
            // Create a MemoryStream that is going to accept the decrypted bytes
            using (var memoryStream = new MemoryStream())
            {
                // Create a symmetric algorithm.
                // We are going to use RijndaelRijndael because it is strong and available on all platforms.
                // You can use other algorithms, to do so substitute the next line with something like
                // TripleDES alg = TripleDES.Create();
                using (var alg = CryptAes(aesKey, mIv))
                {
                    // Create a CryptoStream through which we are going to be pumping our data.
                    // CryptoStreamMode.Write means that we are going to be writing data to the stream
                    // and the output will be written in the MemoryStream we have provided.
                    using (var cs = new CryptoStream(memoryStream, alg.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        // Write the data and make it do the decryption
                        cs.Write(cipherData, 0, cipherData.Length);

                        // Close the crypto stream (or do FlushFinalBlock).
                        // This will tell it that we have done our decryption and there is no more data coming in,
                        // and it is now a good time to remove the padding and finalize the decryption process.
                        cs.FlushFinalBlock();
                        cs.Close();
                    }
                }
                // Now get the decrypted data from the MemoryStream.
                // Some people make a mistake of using GetBuffer() here, which is not the right way.
                var decryptedData = memoryStream.ToArray();
                return decryptedData;
            }
        }
        #endregion
        /*
    * Which works out about 30% faster than PZahras (not that you'd notice with small amounts of data).
    * The BitConverter method itself is pretty quick, it's just having to do the replace which slows it down, so if you can live with the dashes then it's perfectly good.
    */
        public static string ByteArrayToString(byte[] data)
        {
            char[] lookup = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };
            int i = 0, p = 0, l = data.Length;
            char[] c = new char[l * 2 + 2];
            byte d;
            //int p = 2; c[0] = '0'; c[1] = 'x'; //если хотим 0x
            while (i < l)
            {
                d = data[i++];
                c[p++] = lookup[d / 0x10];
                c[p++] = lookup[d % 0x10];
            }
            return new string(c, 0, c.Length);
        }
    }
}

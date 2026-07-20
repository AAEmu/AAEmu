/*
 * by uranusq https://github.com/NL0bP/aaa_emulator
 * by Nikes
 * by NLObP: оригинальный метод шифрации (как в crynetwork.dll)
 */
using System.Security.Cryptography;
using System.Text;

using AAEmu.Commons.IO;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

using NLog;

namespace AAEmu.Commons.Cryptography
{
    public class EncryptionManager : Singleton<EncryptionManager>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly int DwKeySize = 1024;
        // Dictionary of valid keys bound to ConnectionId (unique per session)
        // This prevents encryption mismatches when same account connects from multiple sessions
        private Dictionary<uint, ConnectionKeychain> ConnectionKeys { get; set; }
        public static bool needNewkey2;
        public static bool needNewkey1;
        private static bool AdjustCryptConstantEnable;
        private static string XorKeyValueFilePath;

        public void Load()
        {
            ConnectionKeys = new Dictionary<uint, ConnectionKeychain>();
            LoadCryptConfig();
            Logger.Info("Loaded Encryption Manager.");
        }

        private ConnectionKeychain GetOrCreateConnectionKeys(uint connectionId, ulong accountId)
        {
            // Use ConnectionId as the primary key since it's unique per session
            if (ConnectionKeys.TryGetValue(connectionId, out var keys))
            {
                return keys;
            }

            return GenerateRsaKeyPair(connectionId, accountId);
        }

        private ConnectionKeychain GenerateRsaKeyPair(uint connectionId, ulong accountId)
        {
            // Always remove old keys for this connection first to prevent encryption mismatches
            if (ConnectionKeys.TryGetValue(connectionId, out var oldKeys))
            {
                Logger.Warn("Replacing old RSA key pair for ConnectionId={0}, AccountId={1}",
                    connectionId, oldKeys.RsaKeyPair != null ? accountId : 0);
                ConnectionKeys.Remove(connectionId);
            }

            var rsaKeyPair = new RSACryptoServiceProvider();
            var keys = new ConnectionKeychain(connectionId, rsaKeyPair);
            Logger.Debug("[{0}] Generated new RSA key pair for connection {1}.", accountId, connectionId);
            ConnectionKeys.Add(connectionId, keys);
            return keys;
        }

        /// <summary>
        /// Removes encryption keys by AccountId (legacy method, kept for compatibility)
        /// In the new architecture, keys are stored by ConnectionId, so this method does nothing
        /// </summary>
        [Obsolete("Use RemoveConnectionKeysByConnectionId instead")]
        public void RemoveConnectionKeys(ulong accountId)
        {
            // Legacy method - keys are now stored by ConnectionId, not AccountId
            // Cleanup happens automatically in GenerateRsaKeyPair when connection is reused
            Logger.Debug("RemoveConnectionKeys(ulong) is deprecated. Keys are stored by ConnectionId.");
        }

        /// <summary>
        /// Removes encryption keys by ConnectionId (unique per session)
        /// </summary>
        public void RemoveConnectionKeysByConnectionId(uint connectionId)
        {
            if (ConnectionKeys.Remove(connectionId))
            {
                Logger.Trace("Removed connection keychain for ConnectionId={0}.", connectionId);
            }
        }

        public PacketStream WriteKeyParams(uint connectionId, ulong accountId, PacketStream stream)
        {
            var keychain = GetOrCreateConnectionKeys(connectionId, accountId);
            var rsaParameters = keychain.RsaKeyPair.ExportParameters(false);
            stream.Write(rsaParameters.Modulus);
            stream.Write(new byte[125]);
            stream.Write(rsaParameters.Exponent);
            return stream;
        }

        public void StoreClientKeys(byte[] aesKeyEncrypted, byte[] xorKeyEncrypted, ulong accountId, uint connectionId)
        {
            // Use connectionId as the key since it's unique per session
            if (!ConnectionKeys.TryGetValue((uint)connectionId, out var keys))
            {
                Logger.Warn("StoreClientKeys: No RSA key found for ConnectionId={0}, AccountId={1}", connectionId, accountId);
                return;
            }

            Logger.Warn("AccountId: {0}, ConnectionId: {1}", accountId, connectionId);

            try
            {
                var xorConstRaw = keys.RsaKeyPair.Decrypt(xorKeyEncrypted, false);
                var head = BitConverter.ToUInt32(xorConstRaw, 0);
                Logger.Warn("head: {0}", head); // <-- этот сырой XOR записываем в поле xorConst from AAEMU моего OpcodeFinder`a
                //head = (head ^ 0x15a0248e) * head ^ 0x070f1f23 & 0xffffffff; // 3.0.3.0 archerage.to
                //head = (head ^ 0x15A314A2) * head ^ 0x070F1F23 & 0xffffffff; // 3.0.4.2 AAClassic
                head = (head ^ 0x15351715) * head ^ 0x070F1F23 & 0xffffffff; // 3.5.3.0 AAClassic
                keys.XorKey = head * head & 0xffffffff;
                keys.AesKey = keys.RsaKeyPair.Decrypt(aesKeyEncrypted, false);
                keys.RecievedKeys = true;
                Logger.Warn("AES: {0} XOR: {1}", Helpers.ByteArrayToString(keys.AesKey), keys.XorKey);

                // для автоматического подбора констант
                if (keys.XorKeyConstant1 == 0 || keys.XorKeyConstant2 == 0)
                    LoadXorKeyConstant(keys);
            }
            catch (CryptographicException ex)
            {
                Logger.Error(ex, "Failed to decrypt client keys for AccountId: {0}, ConnectionId: {1}. Possible causes: wrong RSA key, corrupted data, or client version mismatch.", accountId, connectionId);
                // Очищаем ключи, чтобы клиент получил новые при следующем подключении
                ConnectionKeys.Remove((uint)connectionId);
            }
        }

        /// <summary>
        /// Автоматический подбор констант: загрузка из файла
        /// </summary>
        /// <param name="keys"></param>
        private static void LoadXorKeyConstant(ConnectionKeychain keys)
        {
            var worldPath = Path.Combine(FileManager.AppPath, "Configurations");
            XorKeyValueFilePath = Path.Combine(worldPath, "xorKeyValue.txt");
            using var reader = new StreamReader(XorKeyValueFilePath);
            while (!reader.EndOfStream)
            {
                var xorKeyValueLine1 = reader.ReadLine();
                var xorKeyValue1 = reader.ReadLine();
                if (xorKeyValueLine1 == "XorKeyConstant1:")
                {
                    // сохраняем пакеты в список пакетов
                    keys.XorKeyConstant1 = Convert.ToUInt32(xorKeyValue1, 16);
                }
                var xorKeyValueLine2 = reader.ReadLine();
                var xorKeyValue2 = reader.ReadLine();
                if (xorKeyValueLine2 == "XorKeyConstant2:")
                {
                    // сохраняем пакеты в список пакетов
                    keys.XorKeyConstant2 = Convert.ToUInt32(xorKeyValue2, 16);
                }
                // даже если этого значения нет в файле, установите переменную AdjustCryptConstantEnable
                // even if this value is not in the file, set the variable AdjustCryptConstantEnable
                _ = reader.ReadLine();
                var xorKeyValue3 = reader.ReadLine()?.ToLower();
                AdjustCryptConstantEnable = xorKeyValue3 == "true";
            }
        }

        private static void LoadCryptConfig()
        {
            var worldPath = Path.Combine(FileManager.AppPath, "Configurations");
            XorKeyValueFilePath = Path.Combine(worldPath, "xorKeyValue.txt");
            using var reader = new StreamReader(XorKeyValueFilePath);
            while (!reader.EndOfStream)
            {
                _ = reader.ReadLine();
                _ = reader.ReadLine();
                _ = reader.ReadLine();
                _ = reader.ReadLine();
                // даже если этого значения нет в файле, установите переменную AdjustCryptConstantEnable
                // even if this value is not in the file, set the variable AdjustCryptConstantEnable
                _ = reader.ReadLine();
                var xorKeyValue3 = reader.ReadLine()?.ToLower();
                AdjustCryptConstantEnable = xorKeyValue3 == "true";
            }
        }

        public byte GetSCMessageCount(uint connectionId, ulong accountId)
        {
            var keys = GetOrCreateConnectionKeys(connectionId, accountId);
            var mc = keys.SCMessageCount;
            //Logger.Trace("SCMessageCount={0}, connectionId={1}, accountId={2}", mc, connectionId, accountId);
            return mc;
        }

        public void IncSCMsgCount(uint connectionId, ulong accountId)
        {
            var keys = GetOrCreateConnectionKeys(connectionId, accountId);
            keys.SCMessageCount++;
        }

        public byte GetAndIncSCMessageCount(uint connectionId, ulong accountId)
        {
            var keys = GetOrCreateConnectionKeys(connectionId, accountId);
            var mc = keys.SCMessageCount++;
            //Logger.Warn("SCMessageCount={0}, connectionId={1}, accountId={2}", mc, connectionId, accountId);
            return mc;
        }

        #region S->C Encryption
        // Methods for SC packet Encryption
        /// <summary>
        /// Подсчет контрольной суммы пакета, используется в шифровании пакетов DD05 и 0005
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        /// <returns>Crc8</returns>
        private byte Crc8(byte[] data, int size)
        {
            uint checksum = 0;
            for (var i = 0; i < size; i++)
            {
                checksum *= 0x13;
                checksum += data[i];
            }
            return (byte)checksum;
        }

        public byte Crc8(byte[] data)
        {
            return Crc8(data, data.Length);
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
                array[i] = (byte)(bodyPacket[i] ^ Inline(ref cry));
            }
            for (var i = n - offset; i < length; i++)
            {
                array[i] = (byte)(bodyPacket[i] ^ Inline(ref cry));
            }
            return array;
        }
        #endregion

        #region C->S Decryption
        // Methods for CS packet Decryption
        //------------------------------
        // здесь распаковка пакетов от клиента 0005
        // для дешифрации следующих пакетов iv = шифрованный предыдущий пакет
        //------------------------------
        public byte[] Decode(byte[] data, uint connectionId, ulong accountId)
        {
            var keys = GetOrCreateConnectionKeys(connectionId, accountId);
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
                result = 0xFE;
            }
            keys.CSSecondaryOffsetSequence = seq;
            return result;
        }

        private static string ByteArrayToHexString(byte[] bytes)
        {
            var hex = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                hex.AppendFormat("{0:X2}", b);
            }
            return hex.ToString();
        }

        private static byte[] DecodeXor(byte[] bodyPacket, uint xorKey, ConnectionKeychain keys)
        {
            if (AdjustCryptConstantEnable)
            {
                /*
                 * логика подбора такая:
                 * сначала подбираем первую константу для имеющейся второй
                 * если первая 0xFF, то меняем вторую на новую и начинаем подбор первой константы с 0x00
                 */
                var dirty = false;
                // подбираем константы шифрации
                if (keys.XorKeyConstant1 > 0x75A024FF)
                {
                    keys.XorKeyConstant1 = 0x75A02400;
                    dirty = true;
                    needNewkey2 = true;
                }

                if (keys.XorKeyConstant1 == 0 || keys.XorKeyConstant2 == 0)
                {
                    LoadXorKeyConstant(keys);
                }

                if (needNewkey1)
                {
                    needNewkey1 = false;
                    // заменим первую константу
                    keys.XorKeyConstant1++;
                    if (keys.XorKeyConstant1 > 0x75A024FF)
                    {
                        keys.XorKeyConstant1 = 0x75A02400;
                        needNewkey2 = true;
                    }

                    dirty = true;
                }

                if (needNewkey2)
                {
                    needNewkey2 = false;
                    // заменим вторую константу
                    var tuneL = (byte)Random.Shared.Next(0x01, 0xFF);
                    var tuneR = (byte)Random.Shared.Next(0x01, 0xFF);
                    // Исходное uint число с заполнителями NN
                    var result = keys.XorKeyConstant2 & 0x00FFFF00;
                    result |= (uint)tuneL << 24; // Вставляем tuneL в старший байт
                    result |= tuneR; // Вставляем tuneR в младший байт
                    keys.XorKeyConstant2 = result; // Заменяем байты на указанные значения
                    dirty = true;
                }

                if (dirty)
                {
                    using var writer = new StreamWriter(XorKeyValueFilePath, false);
                    writer.WriteLine("XorKeyConstant1:");
                    writer.WriteLine(keys.XorKeyConstant1.ToString("X8"));
                    writer.WriteLine("XorKeyConstant2:");
                    writer.WriteLine(keys.XorKeyConstant2.ToString("X8"));
                    writer.WriteLine("AdjustCryptConstantEnable:");
                    writer.WriteLine(AdjustCryptConstantEnable.ToString());
                }
            }

            //          +-Hash начало блока для DecodeXOR, где второе число, в данном случае F(16 байт)-реальная длина данных в пакете, к примеру A(10 байт)-реальная длина данных в пакете
            //          |  +-начало блока для DecodeAES
            //          V  V
            //1300 0005 3F D831012E6DFA489A268BC6AD5BC69263
            var mBodyPacket = new byte[bodyPacket.Length - 3];
            Buffer.BlockCopy(bodyPacket, 3, mBodyPacket, 0, bodyPacket.Length - 3);
            var msgKey = ((uint)(bodyPacket.Length / 16 - 1) << 4) + (uint)(bodyPacket[2] - 47); // это реальная длина данных в пакете
            var array = new byte[mBodyPacket.Length];
            var mul = msgKey * xorKey; // <-- ставим бряк здесь и смотрим xorKey, packetBody, aesKey, IV для моего OpcodeFinder`a

            //// расскоментируйте блок кода для записи xorKey, packetBody, aesKey, IV для моего OpcodeFinder`a
            //using (StreamWriter writer = new StreamWriter("o:/input.txt", true))
            //{
            //    writer.WriteLine("xorkey:");
            //    writer.WriteLine(keys.XorKey.ToString("X8"));

            //    writer.WriteLine("aesKey:");
            //    writer.WriteLine(ByteArrayToHexString(keys.AesKey));

            //    writer.WriteLine("packetBody:");
            //    writer.WriteLine(ByteArrayToHexString(bodyPacket));
            //}

            // считываем константу из файла и подставляем в формулу шифрации, если константа не установлена, то она будет 0 и не повлияет на результат
            //var cry = mul ^ ((uint)MakeSeq(keys) + 0x75A02453) ^ 0xB27645B4; // 3.5.3.0 AAClassic
            var cry = mul ^ (MakeSeq(keys) + keys.XorKeyConstant1) ^ keys.XorKeyConstant2;

            var seq = keys.CSOffsetSequence;
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
                                if (seq % 11 == 0) { offset = 7; }
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
                array[i] = (byte)(mBodyPacket[i] ^ Add(ref cry));
            }
            for (var i = n; i < mBodyPacket.Length; i++)
            {
                array[i] = (byte)(mBodyPacket[i] ^ Add(ref cry));
            }

            keys.CSOffsetSequence += MakeSeq(keys);
            keys.CSOffsetSequence += 1;
            return array;
        }
        //--------------------------------------------------------------------------------------
        private static Aes CreateAes(byte[] aesKey, byte[] iv)
        {
            var aes = Aes.Create();
            aes.KeySize = 128;
            aes.BlockSize = 128;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;
            aes.Key = aesKey;
            aes.IV = iv;
            return aes;
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
        private static byte[] DecodeAes(byte[] cipherData, byte[] aesKey, byte[] iv)
        {
            var mIv = new byte[16];
            Buffer.BlockCopy(iv, 0, mIv, 0, 16);
            var len = cipherData.Length / 16;
            //Save last 16 bytes in IV
            Buffer.BlockCopy(cipherData, (len - 1) * 16, iv, 0, 16);
            // Create a MemoryStream that is going to accept the decrypted bytes
            using var memoryStream = new MemoryStream();
            // Create a symmetric algorithm.
            // We are going to use Aes because it is strong and available on all platforms.
            using (var alg = CreateAes(aesKey, mIv))
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
        #endregion
    }
}

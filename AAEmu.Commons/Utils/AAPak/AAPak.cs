using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;

// Source: https://github.com/ZeromusXYZ/AAEmu-Packer

namespace AAEmu.Commons.Utils.AAPak
{
    /// <summary>
    /// File Details Block
    /// </summary>
    public class AAPakFileInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x108)] public string name;
        public Int64 offset;
        public Int64 size;
        public Int64 sizeDuplicate; // maybe compressed data size ? if used, observed always same as size
        public Int32 paddingSize; // number of bytes of free space left until the next blocksize of 512 (or space until next file)
        public byte[] md5; // this should be 16 bytes
        public UInt32 dummy1; // looks like padding, mostly 0 or 0x80000000 observed, possible file flags ?
        public Int64 createTime;
        public Int64 modifyTime;
        public UInt64 dummy2; // looks like padding to fill out the block, observed 0
        // The following are not part of the structure but used by the program
        public int entryIndexNumber = -1;
        public int deletedIndexNumber = -1;
    }

    /// <summary>
    /// Pak Header Information
    /// </summary>
    public class AAPakFileHeader
    {
        /// <summary>
        /// Default AES128 key used by XLGames for ArcheAge as encryption key for header and fileinfo data
        /// 32 1F 2A EE AA 58 4A B4 9A 6C 9E 09 D5 9E 9C 6F
        /// </summary>
        private readonly byte[] XLGamesKey = new byte[] { 0x32, 0x1F, 0x2A, 0xEE, 0xAA, 0x58, 0x4A, 0xB4, 0x9A, 0x6C, 0x9E, 0x09, 0xD5, 0x9E, 0x9C, 0x6F };
        /// <summary>
        /// Current encryption key
        /// </summary>
        private byte[] key;
        protected static readonly int headerSize = 0x200;
        protected static readonly int fileInfoSize = 0x150;
        /// <summary>
        /// Memory stream that holds the encrypted file information + header part of the file
        /// </summary>
        public MemoryStream FAT = new MemoryStream();

        public AAPak _owner;
        public int Size = headerSize;
        public long FirstFileInfoOffset;
        public long AddFileOffset;
        public byte[] rawData = new byte[headerSize]; // unencrypted header
        public byte[] data = new byte[headerSize]; // decrypted header data
        public bool isValid;
        /// <summary>
        /// Number of used files inside this pak
        /// </summary>
        public uint fileCount;
        /// <summary>
        /// Number of unused "deleted" files inside this pak
        /// </summary>
        public uint extraFileCount;

        /// <summary>
        /// Empty MD5 Hash to compare against
        /// </summary>
        public static byte[] nullHash = new byte[16];
        /// <summary>
        /// Empty MD5 Hash as a hex string to compare against
        /// </summary>
        public static string nullHashString = "".PadRight(32, '0');
        public static string LastAESError = string.Empty;

        /// <summary>
        /// Creates a new Header Block for a Pak file
        /// </summary>
        /// <param name="owner">The AAPak that this header belongs to</param>
        public AAPakFileHeader(AAPak owner)
        {
            _owner = owner;
            SetCustomKey(XLGamesKey);
        }

        ~AAPakFileHeader()
        {
            // FAT.Dispose();
        }

        /// <summary>
        /// If you want to use custom keys on your pak file, use this function to change the key that is used for encryption/decryption of the FAT and header data
        /// </summary>
        /// <param name="newKey"></param>
        public void SetCustomKey(byte[] newKey)
        {
            key = new byte[newKey.Length];
            newKey.CopyTo(key, 0);
        }

        /// <summary>
        /// Reverts back to the original encryption key, this function is also automatically called when closing a file
        /// </summary>
        public void SetDefaultKey()
        {
            XLGamesKey.CopyTo(key, 0);
        }

        /// <summary>
        /// Encrypts or Decrypts a byte array using AES128 CBC - 
        /// SourceCode: https://stackoverflow.com/questions/44782910/aes128-decryption-in-c-sharp
        /// </summary>
        /// <param name="message">Byte array to process</param>
        /// <param name="key">Encryption key to use</param>
        /// <param name="doEncryption">False = Decrypt, True = Encrypt</param>
        /// <returns>Returns a new byte array containing the processed data</returns>
        public static byte[] EncryptAES(byte[] message, byte[] key, bool doEncryption)
        {
            try
            {
                Aes aes = Aes.Create();
                aes.Key = key;
                aes.IV = new byte[16];
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;

                ICryptoTransform cipher;

                if (doEncryption)
                    cipher = aes.CreateEncryptor();
                else
                    cipher = aes.CreateDecryptor();

                return cipher.TransformFinalBlock(message, 0, message.Length);
            }
            catch (Exception x)
            {
                LastAESError = x.Message;
                return null;
            }
        }

        public static bool EncryptStreamAes(Stream source, Stream target, byte[] key, bool doEncryption, bool leaveOpen = false)
        {
            try
            {
                Aes aes = Aes.Create();
                aes.Key = key;
                aes.IV = new byte[16];
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;

                ICryptoTransform cipher;

                if (doEncryption)
                    cipher = aes.CreateEncryptor();
                else
                    cipher = aes.CreateDecryptor();

                // Create the streams used for encryption.

                CryptoStream csEncrypt = new CryptoStream(target, cipher, CryptoStreamMode.Write);
                source.CopyTo(csEncrypt);
                if (!leaveOpen)
                    csEncrypt.Dispose();

                /*
                using (CryptoStream csEncrypt = new CryptoStream(target, cipher, CryptoStreamMode.Write))
                {
                    source.CopyTo(csEncrypt);
                }
                */
                return true;
            }
            catch (Exception x)
            {
                LastAESError = x.Message;
                return false;
            }
        }

        public static bool EncryptStreamAESWithIV(Stream source, Stream target, byte[] key, byte[] customIV, bool doEncryption)
        {
            try
            {
                Aes aes = Aes.Create();
                aes.Key = key;
                aes.IV = customIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;

                ICryptoTransform cipher;

                if (doEncryption)
                    cipher = aes.CreateEncryptor();
                else
                    cipher = aes.CreateDecryptor();

                // Create the streams used for encryption.
                using (CryptoStream csEncrypt = new CryptoStream(target, cipher, CryptoStreamMode.Write))
                {
                    source.CopyTo(csEncrypt);
                }
                return true;
            }
            catch (Exception x)
            {
                LastAESError = x.Message;
                return false;
            }
        }

        /// <summary>
        /// Same as the EncryptAES but specifying a specific IV
        /// </summary>
        /// <param name="message"></param>
        /// <param name="key"></param>
        /// <param name="customIV"></param>
        /// <param name="doEncryption"></param>
        /// <returns></returns>
        public static byte[] EncryptAESUsingIV(byte[] message, byte[] key, byte[] customIV, bool doEncryption)
        {
            try
            {
                Aes aes = Aes.Create();
                aes.Key = key;
                aes.IV = customIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None;

                ICryptoTransform cipher;

                if (doEncryption == true)
                    cipher = aes.CreateEncryptor();
                else
                    cipher = aes.CreateDecryptor();

                return cipher.TransformFinalBlock(message, 0, message.Length);
            }
            catch (Exception x)
            {
                LastAESError = x.Message;
                return null;
            }
        }

        /// <summary>
        /// Locate and load the encrypted FAT data into memory
        /// </summary>
        /// <returns>Returns true on success</returns>
        public bool LoadRawFAT()
        {
            // Read all File Table Data into Memory
            FAT.SetLength(0);

            long TotalFileInfoSize = (fileCount + extraFileCount) * fileInfoSize;
            _owner._gpFileStream.Seek(0, SeekOrigin.End);
            FirstFileInfoOffset = _owner._gpFileStream.Position;

            // Search for the first file location, it needs to be alligned to a 0x200 size block
            FirstFileInfoOffset -= headerSize;
            FirstFileInfoOffset -= TotalFileInfoSize;
            var dif = FirstFileInfoOffset % 0x200;
            // Align to previous block of 512 bytes
            FirstFileInfoOffset -= dif;

            _owner._gpFileStream.Position = FirstFileInfoOffset;

            SubStream _fat = new SubStream(_owner._gpFileStream, FirstFileInfoOffset, _owner._gpFileStream.Length - FirstFileInfoOffset);
            _fat.CopyTo(FAT);

            return true;
        }

        /// <summary>
        /// Writes current files info back into FAT (encrypted)
        /// </summary>
        /// <returns>Returns true on success</returns>
        public bool WriteToFAT()
        {
            if (_owner.PakType == PakFileType.CSV)
                return false;

            // Read all File Table Data into Memory
            FAT.SetLength(0);

            int bufSize = 0x150; // Marshal.SizeOf(typeof(AAPakFileInfo));
            MemoryStream ms = new MemoryStream(bufSize); // Could probably do without the intermediate memorystream, but it's easier to process
            BinaryWriter writer = new BinaryWriter(ms);

            // Init File Counts
            var totalFileCount = _owner.files.Count + _owner.extraFiles.Count;
            var filesToGo = _owner.files.Count;
            var extrasToGo = _owner.extraFiles.Count;
            int fileIndex = 0;
            int extrasIndex = 0;
            for (int i = 0; i < totalFileCount; i++)
            {
                ms.Position = 0;

                AAPakFileInfo pfi = null;

                if ((_owner.PakType == PakFileType.TypeA) || (_owner.PakType == PakFileType.TypeF))
                {
                    // TypeA has files first, extra files after that
                    if (filesToGo > 0)
                    {
                        filesToGo--;
                        pfi = _owner.files[fileIndex];
                        fileIndex++;
                    }
                    else
                    if (extrasToGo > 0)
                    {
                        extrasToGo--;
                        pfi = _owner.extraFiles[extrasIndex];
                        extrasIndex++;
                    }
                    else
                    {
                        // If we get here, your PC cannot math and something went wrong
                        pfi = null;
                        break;
                    }
                }
                else
                if (_owner.PakType == PakFileType.TypeB)
                {
                    // TypeB has files first, extra files after that
                    if (extrasToGo > 0)
                    {
                        extrasToGo--;
                        pfi = _owner.extraFiles[extrasIndex];
                        extrasIndex++;
                    }
                    else
                    if (filesToGo > 0)
                    {
                        filesToGo--;
                        pfi = _owner.files[fileIndex];
                        fileIndex++;
                    }
                    else
                    {
                        // If we get here, your PC cannot math and something went wrong
                        pfi = null;
                        break;
                    }
                }
                else
                {
                    // Unsupported Type somehow
                    throw new Exception("Don't know how to write this FAT: " + _owner.PakType);
                }

                if (_owner.PakType == PakFileType.TypeA)
                {
                    // Manually write the string for filename
                    for (int c = 0; c < 0x108; c++)
                    {
                        byte ch = 0;
                        if (c < pfi.name.Length)
                            ch = (byte)pfi.name[c];
                        writer.Write(ch);
                    }
                    writer.Write(pfi.offset);
                    writer.Write(pfi.size);
                    writer.Write(pfi.sizeDuplicate);
                    writer.Write(pfi.paddingSize);
                    writer.Write(pfi.md5);
                    writer.Write(pfi.dummy1);
                    writer.Write(pfi.createTime);
                    writer.Write(pfi.modifyTime);
                    writer.Write(pfi.dummy2);
                }
                else
                if (_owner.PakType == PakFileType.TypeB)
                {
                    writer.Write(pfi.paddingSize);
                    writer.Write(pfi.md5);
                    writer.Write(pfi.dummy1);
                    writer.Write(pfi.size);

                    // Manually write the string for filename
                    for (int c = 0; c < 0x108; c++)
                    {
                        byte ch = 0;
                        if (c < pfi.name.Length)
                            ch = (byte)pfi.name[c];
                        writer.Write(ch);
                    }
                    writer.Write(pfi.sizeDuplicate);
                    writer.Write(pfi.offset);
                    writer.Write(pfi.modifyTime);
                    writer.Write(pfi.createTime);
                    writer.Write(pfi.dummy2);
                }
                else
                if (_owner.PakType == PakFileType.TypeF)
                {
                    writer.Write(pfi.dummy2);
                    // Manually write the string for filename
                    for (int c = 0; c < 0x108; c++)
                    {
                        byte ch = 0;
                        if (c < pfi.name.Length)
                            ch = (byte)pfi.name[c];
                        writer.Write(ch);
                    }
                    writer.Write(pfi.offset);
                    writer.Write(pfi.size);
                    writer.Write(pfi.sizeDuplicate);
                    writer.Write(pfi.paddingSize);
                    writer.Write(pfi.md5);
                    writer.Write(pfi.dummy1);
                    writer.Write(pfi.createTime);
                    writer.Write(pfi.modifyTime); // For TypeF this is typically zero
                }
                else
                {
                    throw new Exception("I don't know how to write this file format: " + _owner.PakType);
                }

                // encrypt and write our new file into the FAT memory stream
                byte[] decryptedFileData = new byte[bufSize];
                ms.Position = 0;
                ms.Read(decryptedFileData, 0, bufSize);
                byte[] rawFileData = EncryptAES(decryptedFileData, key, true); // encrypt header data
                FAT.Write(rawFileData, 0, bufSize);
            }
            ms.Dispose();

            // Calculate padding to header
            var dif = (FAT.Length % 0x200);
            if (dif > 0)
            {
                var pad = (0x200 - dif);
                FAT.SetLength(FAT.Length + pad);
                FAT.Position = FAT.Length;
            }
            // Update header info
            fileCount = (uint)_owner.files.Count;
            extraFileCount = (uint)_owner.extraFiles.Count;
            // Stretch size for header
            FAT.SetLength(FAT.Length + headerSize);
            // Encrypt the Header data
            EncryptHeaderData();
            // Write encrypted header
            FAT.Write(rawData, 0, 0x20);

            return true;
        }

        /// <summary>
        /// Read and decrypt the File Details Table that was loaded into the FAT MemoryStream
        /// </summary>
        public void ReadFileTable()
        {
            // Check aa.bms QuickBMS file for reference
            FAT.Position = 0;

            int bufSize = 0x150; // Marshal.SizeOf(typeof(AAPakFileInfo));
            MemoryStream ms = new MemoryStream(bufSize); // Could probably do without the intermediate memorystream, but it's easier to process
            BinaryReader reader = new BinaryReader(ms);
            
            // Read the Files
            _owner.files.Clear();
            _owner.extraFiles.Clear();
            var totalFileCount = fileCount + extraFileCount;
            var filesToGo = fileCount;
            var extraToGo = extraFileCount;
            var fileIndexCounter = -1;
            var deletedIndexCounter = -1;
            for (uint i = 0; i < totalFileCount; i++)
            {
                // Read and decrypt a fileinfo block
                byte[] rawFileData = new byte[bufSize]; // decrypted header data
                FAT.Read(rawFileData, 0, bufSize);
                byte[] decryptedFileData = EncryptAES(rawFileData, key, false);

                // Read decrypted data into a AAPakFileInfo
                ms.SetLength(0);
                ms.Write(decryptedFileData, 0, bufSize);
                ms.Position = 0;
                AAPakFileInfo pfi = new AAPakFileInfo();
                if (_owner.PakType == PakFileType.TypeA)
                {
                    // Manually read the string for filename
                    pfi.name = "";
                    for (int c = 0; c < 0x108; c++)
                    {
                        byte ch = reader.ReadByte();
                        if (ch != 0)
                            pfi.name += (char)ch;
                        else
                            break;
                    }
                    ms.Position = 0x108;
                    pfi.offset = reader.ReadInt64();
                    pfi.size = reader.ReadInt64();
                    pfi.sizeDuplicate = reader.ReadInt64();
                    pfi.paddingSize = reader.ReadInt32();
                    pfi.md5 = reader.ReadBytes(16);
                    pfi.dummy1 = reader.ReadUInt32(); // observed 0x00000000
                    pfi.createTime = reader.ReadInt64();
                    pfi.modifyTime = reader.ReadInt64();
                    pfi.dummy2 = reader.ReadUInt64(); // unused ?
                }
                else
                if (_owner.PakType == PakFileType.TypeB)
                {
                    pfi.paddingSize = reader.ReadInt32();
                    pfi.md5 = reader.ReadBytes(16);
                    pfi.dummy1 = reader.ReadUInt32(); // 0x80000000
                    pfi.size = reader.ReadInt64();
                    // Manually read the string for filename
                    pfi.name = "";
                    for (int c = 0; c < 0x108; c++)
                    {
                        byte ch = reader.ReadByte();
                        if (ch != 0)
                            pfi.name += (char)ch;
                        else
                            break;
                    }
                    ms.Position = 0x128;
                    pfi.sizeDuplicate = reader.ReadInt64();
                    pfi.offset = reader.ReadInt64();
                    pfi.modifyTime = reader.ReadInt64();
                    pfi.createTime = reader.ReadInt64();
                    pfi.dummy2 = reader.ReadUInt64(); // unused ?
                }
                else
                if (_owner.PakType == PakFileType.TypeF)
                {
                    pfi.dummy2 = reader.ReadUInt64(); // unused ?
                    // Manually read the string for filename
                    pfi.name = "";
                    for (int c = 0; c < 0x108; c++)
                    {
                        byte ch = reader.ReadByte();
                        if (ch != 0)
                            pfi.name += (char)ch;
                        else
                            break;
                    }
                    ms.Position = 0x110;

                    pfi.offset = reader.ReadInt64();
                    pfi.size = reader.ReadInt64();
                    pfi.sizeDuplicate = reader.ReadInt64();
                    pfi.paddingSize = reader.ReadInt32();
                    pfi.md5 = reader.ReadBytes(16);
                    pfi.dummy1 = reader.ReadUInt32(); // observed 0x00000000
                    pfi.createTime = reader.ReadInt64();
                    pfi.modifyTime = reader.ReadInt64(); // For TypeF this is typically zero
                }
                else
                {
                    /*
                    using (var hf = File.OpenWrite("fileheader.bin"))
                    {
                        ms.CopyTo(hf);
                    }
                    ms.Position = 0;
                    */
                }

                if ((_owner.PakType == PakFileType.TypeA) || (_owner.PakType == PakFileType.TypeF))
                {
                    // TypeA has files first and extra files last
                    if (filesToGo > 0)
                    {
                        fileIndexCounter++;
                        pfi.entryIndexNumber = fileIndexCounter;

                        filesToGo--;
                        _owner.files.Add(pfi);
                    }
                    else
                    if (extraToGo > 0)
                    {
                        // "Extra" Files. It looks like these are old deleted files renamed to "__unused__"
                        // There might be more to these, but can't be sure at this moment, looks like they are 512 byte blocks on my paks
                        deletedIndexCounter++;
                        pfi.deletedIndexNumber = deletedIndexCounter;

                        extraToGo--;
                        _owner.extraFiles.Add(pfi);
                    }
                }
                else
                if (_owner.PakType == PakFileType.TypeB)
                {
                    // TypeB has extra files first and normal files last
                    if (extraToGo > 0)
                    {
                        fileIndexCounter++;
                        pfi.entryIndexNumber = fileIndexCounter;

                        extraToGo--;
                        _owner.extraFiles.Add(pfi);
                    }
                    else
                    if (filesToGo > 0)
                    {
                        deletedIndexCounter++;
                        pfi.deletedIndexNumber = deletedIndexCounter;

                        filesToGo--;
                        _owner.files.Add(pfi);
                    }
                }
                else
                {
                    // Call the police, illegal Types are invading our safespace
                }

                /*
                // Debug stuff
                if (pfi.name == "bin32/archeage.exe")
                {
                    ByteArrayToHexFile(decryptedFileData, "file-"+ i.ToString() + ".hex");
                    File.WriteAllBytes("file-" + i.ToString() + ".bin",decryptedFileData);
                }
                */

                // Update our "end of file data" location if needed
                if ((pfi.offset + pfi.size + pfi.paddingSize) > AddFileOffset)
                {
                    AddFileOffset = pfi.offset + pfi.size + pfi.paddingSize;
                }
            }

            ms.Dispose();
        }


        /// <summary>
        /// Helper function for debugging, write byte array as a hex text file
        /// </summary>
        /// <param name="bytes"></param>
        /// <param name="fileName"></param>
        private static void ByteArrayToHexFile(byte[] bytes,string fileName)
        {
            string s = "";
            for(int i = 0; i < bytes.Length;i++)
            {
                s += bytes[i].ToString("X2") + " ";
                if ((i % 16) == 15)
                    s += "\r\n";
                else
                {
                    if ((i % 4) == 3)
                        s += " ";
                    if ((i % 8) == 7)
                        s += " ";
                }
            }
            File.WriteAllText(fileName, s);
        }

        /// <summary>
        /// Helper function for debugging, write byte array as a hex text file
        /// </summary>
        /// <param name="bytes"></param>
        public static string ByteArrayToHexString(byte[] bytes, string spacingText = " ", string lineFeed = "\r\n")
        {
            string s = "";
            for(int i = 0; i < bytes.Length;i++)
            {
                s += bytes[i].ToString("X2") + spacingText;
                if ((i % 16) == 15)
                    s += lineFeed;
                else
                {
                    if ((i % 4) == 3)
                        s += spacingText;
                    if ((i % 8) == 7)
                        s += spacingText;
                }
            }

            return s;
        }


        /// <summary>
        /// Decrypt the current header data to get the file counts
        /// </summary>
        public void DecryptHeaderData()
        {
            data = EncryptAES(rawData, key, false);

            // A valid header/footer is check by it's identifier
            if ((data[0] == 'W') && (data[1] == 'I') && (data[2] == 'B') && (data[3] == 'O'))
            {
                // W I B O = 0x57 0x49 0x42 0x4F
                _owner.PakType = PakFileType.TypeA;
                fileCount = BitConverter.ToUInt32(data, 8);
                extraFileCount = BitConverter.ToUInt32(data, 12);
                isValid = true;
            }
            else
            if ((data[8] == 'I') && (data[9] == 'D') && (data[10] == 'E') && (data[11] == 'J'))
            {
                // I D E J = 0x49 0x44 0x45 0x4A
                _owner.PakType = PakFileType.TypeB;
                fileCount = BitConverter.ToUInt32(data, 12);
                extraFileCount = BitConverter.ToUInt32(data, 0);
                isValid = true;
            }
            else
            if ((data[0] == 'Z') && (data[1] == 'E') && (data[2] == 'R') && (data[3] == 'O'))
            {
                // Z E R O = 0x5A 0x45 0x52 0x4F
                _owner.PakType = PakFileType.TypeF;
                fileCount = BitConverter.ToUInt32(data, 8);
                extraFileCount = BitConverter.ToUInt32(data, 12);
                isValid = true;
            }
            else
            {
                // Doesn't look like this is a pak file, the file is corrupted, or is in a unknown layout/format
                fileCount = 0;
                extraFileCount = 0;
                isValid = false;

                if (_owner.DebugMode)
                {
                    var hex = ByteArrayToHexString(key, "", "");
                    File.WriteAllBytes("game_pak_failed_header_" + hex + ".key", data);
                }
            }

        }

        /// <summary>
        /// Encrypt the current header data
        /// </summary>
        public void EncryptHeaderData()
        {
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, headerSize);
            ms.Position = 0;
            BinaryWriter writer = new BinaryWriter(ms);

            if (_owner.PakType == PakFileType.TypeA)
            {
                writer.Write((byte)'W');
                writer.Write((byte)'I');
                writer.Write((byte)'B');
                writer.Write((byte)'O');
                writer.Seek(8, SeekOrigin.Begin);
                writer.Write(fileCount);
                writer.Seek(12, SeekOrigin.Begin);
                writer.Write(extraFileCount);
            }
            else
            if (_owner.PakType == PakFileType.TypeB)
            {
                writer.Write(extraFileCount);
                writer.Seek(8, SeekOrigin.Begin);
                writer.Write((byte)'I');
                writer.Write((byte)'D');
                writer.Write((byte)'E');
                writer.Write((byte)'J');
                writer.Seek(12, SeekOrigin.Begin);
                writer.Write(fileCount);
            }
            else
            if (_owner.PakType == PakFileType.TypeF)
            {
                writer.Write((byte)'Z');
                writer.Write((byte)'E');
                writer.Write((byte)'R');
                writer.Write((byte)'O');
                writer.Seek(8, SeekOrigin.Begin);
                writer.Write(fileCount);
                writer.Seek(12, SeekOrigin.Begin);
                writer.Write(extraFileCount);
            }
            else
            {
                // I don't know what to do with something that shouldn't exist
            }

            ms.Position = 0;
            ms.Read(data, 0, headerSize);
            ms.Dispose();
            // Encrypted our stored data into rawData
            rawData = EncryptAES(data, key, true);
        }

    }

    public enum PakFileType { TypeA, TypeB, CSV, TypeF };

    /// <summary>
    /// AAPak Class used to handle game_pak from ArcheAge
    /// </summary>
    public class AAPak
    {
        /// <summary>
        /// Virtual data to return as a null value for file details, can be used as to initialize a var to pass as a ref
        /// </summary>
        public AAPakFileInfo nullAAPakFileInfo = new AAPakFileInfo();
        public string _gpFilePath { get; private set; }
        public FileStream _gpFileStream { get; private set; }
        /// <summary>
        /// points to this pakfile's header
        /// </summary>
        public AAPakFileHeader _header;
        /// <summary>
        /// Checks if current pakfile information is loaded into memory
        /// </summary>
        public bool isOpen = false;
        /// <summary>
        /// Set to true if there have been changes made that require a rewrite of the FAT and/or header
        /// </summary>
        public bool isDirty = false;
        /// <summary>
        /// Set to true if this is not a pak file, but rather information loaded from somewhere else
        /// </summary>
        public bool isVirtual = false;
        /// <summary>
        /// List of all used files
        /// </summary>
        public List<AAPakFileInfo> files = new List<AAPakFileInfo>();
        /// <summary>
        /// List of all unused files, normally these are all named "__unused__"
        /// </summary>
        public List<AAPakFileInfo> extraFiles = new List<AAPakFileInfo>();
        /// <summary>
        /// Virtual list of all folder names, use GenerateFolderList() to populate this list (might take a while)
        /// </summary>
        public List<string> folders = new List<string>();
        /// <summary>
        /// Show if this pakfile is opened in read-only mode
        /// </summary>
        public bool readOnly { get; private set; }
        /// <summary>
        /// If set to true, adds the freed space from a delete to the previous file's padding. 
        /// If false (default), it "moves" the file into extraFiles for freeing up space, allowing it to be reused instead.
        /// Should only need to be changed if you are writing your own specialized patcher, and only in special cases
        /// </summary>
        public bool paddingDeleteMode = false;
        public PakFileType PakType = PakFileType.TypeA ;
        public bool DebugMode = false;

        /// <summary>
        /// Creates and/or opens a game_pak file
        /// </summary>
        /// <param name="filePath">Filename of the pak</param>
        /// <param name="openAsReadOnly">Open pak in readOnly Mode if true. Ignored if createAsNewPak is set</param>
        /// <param name="createAsNewPak">If true, openAsReadOnly is ignored and will create a new pak at filePath location in read/write mode. Warning: This will overwrite any existing pak at that location !</param>
        public AAPak(string filePath, bool openAsReadOnly = true, bool createAsNewPak = false)
        {
            _header = new AAPakFileHeader(this);
            if (filePath != "")
            {
                bool isLoaded = false;

                /*
                var ext = Path.GetExtension(filePath).ToLower();
                if (ext == "csv") 
                {
                    if ((openAsReadOnly == true) && (createAsNewPak == false))
                    {
                        // Open file as CVS data
                        isLoaded = OpenVirtualCSVPak(filePath);
                        return;
                    }
                    // We will only allow opening as a CVS file when it's set to readonly (and not a new file)
                }
                */

                if (createAsNewPak)
                {
                    isLoaded = NewPak(filePath);
                }
                else
                {
                    isLoaded = OpenPak(filePath, openAsReadOnly);
                }
                if (isLoaded)
                {
                    isOpen = ReadHeader();
                }
                else
                {
                    isOpen = false;
                }
            }
            else
            {
                isOpen = false;
            }
        }

        ~AAPak()
        {
            if (isOpen)
                ClosePak();
        }

        /// <summary>
        /// Opens a pak file, can only be used if no other file is currently loaded
        /// </summary>
        /// <param name="filePath">Filename of the pakfile to open</param>
        /// <param name="openAsReadOnly">Set to true to open the pak in read-only mode</param>
        /// <returns>Returns true on success, or false if something failed</returns>
        public bool OpenPak(string filePath, bool openAsReadOnly)
        {
            // Fail if already open
            if (isOpen)
                return false;

            // Check if it exists
            if (!File.Exists(filePath))
            {
                return false;
            }

            isVirtual = false;

            var ext = Path.GetExtension(filePath).ToLower();
            if (ext == ".csv")
            {
                openAsReadOnly = true;
                readOnly = true;
                // Open file as CVS data
                return OpenVirtualCSVPak(filePath);
            }

            try
            {
                // Open stream
                if (openAsReadOnly)
                {
                    _gpFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                }
                else
                {
                    _gpFileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
                }
                _gpFilePath = filePath;
                isDirty = false;
                isOpen = true;
                readOnly = openAsReadOnly;
                return ReadHeader();
            }
            catch
            {
                _gpFilePath = null ;
                isOpen = false;
                readOnly = true;
                return false;
            }
        }

        /// <summary>
        /// Creates a new pakfile with name filename, will overwrite a existing file if it exists
        /// </summary>
        /// <param name="filePath">Filename of the new pakfile</param>
        /// <returns>Returns true on success, or false if something went wrong, or if you still have a pakfile open</returns>
        public bool NewPak(string filePath)
        {
            // Fail if already open
            if (isOpen)
                return false;
            isVirtual = false;
            try
            {
                // Create new file stream
                _gpFileStream = new FileStream(filePath, FileMode.Create,FileAccess.ReadWrite);
                _gpFilePath = filePath;
                readOnly = false;
                isOpen = true;
                isDirty = true;
                SaveHeader(); // Save blank data
                return ReadHeader(); // read blank data to confirm
            }
            catch
            {
                _gpFilePath = null;
                isOpen = false;
                readOnly = true;
                return false;
            }
        }


        public bool OpenVirtualCSVPak(string csvfilePath)
        {            
            // Fail if already open
            if (isOpen)
                return false;

            // Check if it exists
            if (!File.Exists(csvfilePath))
            {
                return false;
            }
            isVirtual = true;
            _gpFileStream = null; // Not used on virtual paks
            try
            {
                // Open stream
                _gpFilePath = csvfilePath;
                isDirty = false;
                isOpen = true;
                readOnly = true;
                PakType = PakFileType.CSV;
                return ReadCSVData();
            }
            catch
            {
                isOpen = false;
                readOnly = true;
                return false;
            }
        }

        /// <summary>
        /// Closes the currently opened pakfile (if open)
        /// </summary>
        public void ClosePak()
        {
            if (!isOpen)
                return;
            if ((isDirty) && (readOnly == false))
                SaveHeader();
            if (_gpFileStream != null)
                _gpFileStream.Close();
            _gpFileStream = null;
            _gpFilePath = null;
            isOpen = false;
            _header.SetDefaultKey();
        }

        /// <summary>
        /// Encrypts and saves Header and File Information Table back to the pak. 
        /// This is also automatically called on ClosePak() if changes where made.
        /// Warning: Failing to save might corrupt your pak if files were added or deleted !
        /// </summary>
        public void SaveHeader()
        {
            _header.WriteToFAT();
            _gpFileStream.Position = _header.FirstFileInfoOffset;
            _header.FAT.Position = 0;
            _header.FAT.CopyTo(_gpFileStream);
            _gpFileStream.SetLength(_gpFileStream.Position);

            isDirty = false;
        }

        /// <summary>
        /// Read Pak Header and FAT
        /// </summary>
        /// <returns>Returns true if the read information makes a valid pakfile</returns>
        protected bool ReadHeader()
        {
            files.Clear();
            extraFiles.Clear();
            folders.Clear();

            // Read the last 512 bytes as raw header data
            _gpFileStream.Seek(-_header.Size, SeekOrigin.End);

            // Mark correct location as header offset location
            _gpFileStream.Read(_header.rawData, 0, _header.Size); // We don't need to read the entire thing, just the first 32 bytes contain data
            // _gpFileStream.Read(_header.rawData, 0, _header.Size);

            _header.DecryptHeaderData();

            if (_header.isValid)
            {
                // Only allow editing for TypeA
                // if (PakType != PakFileType.PakTypeA) readOnly = true;
                _header.LoadRawFAT();
                _header.ReadFileTable();
            }
            else
            {
                _header.FAT.SetLength(0);
            }

            return _header.isValid ;
        }

        public static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        public static string DateTimeToDateTimeStr(DateTime aTime)
        {
            string res = "";
            try
            {
                res = aTime.ToString("yyyyMMdd-HHmmss");
            }
            catch
            {
                res = "00000000-000000";
            }
            return res;
        }

        /// <summary>
        /// Creates a file time from a given specialized string
        /// </summary>
        /// <param name="encodedString"></param>
        /// <returns>FILETIME as UTC</returns>
        public static long DateTimeStrToFILETIME(string encodedString)
        {
            long res = 0;

            int yyyy = 0;
            int mm = 0;
            int dd = 0;
            int hh = 0;
            int nn = 0;
            int ss = 0;

            try
            {
                if (!int.TryParse(encodedString.Substring(0, 4), out yyyy)) yyyy = 0;
                if (!int.TryParse(encodedString.Substring(5, 2), out mm)) mm = 0;
                if (!int.TryParse(encodedString.Substring(8, 2), out dd)) dd = 0;
                if (!int.TryParse(encodedString.Substring(11, 2), out hh)) hh = 0;
                if (!int.TryParse(encodedString.Substring(14, 2), out nn)) nn = 0;
                if (!int.TryParse(encodedString.Substring(17, 2), out ss)) ss = 0;

                res = (new DateTime(yyyy, mm, dd, hh, nn, ss)).ToFileTimeUtc();
            }
            catch
            {
                res = 0;
            }
            return res;
        }

        protected bool ReadCSVData()
        {
            files.Clear();
            extraFiles.Clear();
            folders.Clear();

            var lines = File.ReadAllLines(_gpFilePath);

            if (lines.Length >= 1)
            {
                string csvHead = "";
                csvHead = "name";
                csvHead += ";size";
                csvHead += ";offset";
                csvHead += ";md5";
                csvHead += ";createTime";
                csvHead += ";modifyTime";
                csvHead += ";sizeDuplicate";
                csvHead += ";paddingSize";
                csvHead += ";dummy1";
                csvHead += ";dummy2";

                if (lines[0].ToLower() != csvHead)
                {
                    _header.isValid = true;
                }
                else
                {
                    _header.isValid = false;
                }
            }
            else
            {
                _header.isValid = false;
            }

            if (_header.isValid)
            {
                for (var i = 1; i < lines.Length;i++)
                {
                    var line = lines[i];
                    var fields = line.Split(';');
                    if (fields.Length == 10)
                    {
                        try
                        {
                            var fni = new AAPakFileInfo();

                            // Looks like it's valid, read it
                            fni.name = fields[0];
                            fni.size = long.Parse(fields[1]);
                            fni.offset = long.Parse(fields[2]);
                            fni.md5 = StringToByteArray(fields[3]);
                            fni.createTime = DateTimeStrToFILETIME(fields[4]);
                            fni.modifyTime = DateTimeStrToFILETIME(fields[5]);
                            fni.sizeDuplicate = long.Parse(fields[6]);
                            fni.paddingSize = int.Parse(fields[7]);
                            fni.dummy1 = uint.Parse(fields[8]);
                            fni.dummy2 = uint.Parse(fields[9]);

                            // TODO: check if this reads correctly
                            files.Add(fni);
                        }
                        catch
                        {
                            _header.isValid = false;
                            return false;
                        }
                    }
                }
            }

            return _header.isValid;
        }


        /// <summary>
        /// Populate the folders string list with virual folder names derived from the files found inside the pak
        /// </summary>
        /// <param name="sortTheList">Set to false if you don't want the resulting folders list to be sorted (not recommended)</param>
        public void GenerateFolderList(bool sortTheList = true)
        {
            // There is no actual directory info stored in the pak file, so we just generate it based on filenames
            folders.Clear();
            if (!isOpen || !_header.isValid) return;
            foreach(AAPakFileInfo pfi in files)
            {
                if (pfi.name == string.Empty)
                    continue;
                try
                {
                    // Horror function, I know :p
                    string n = Path.GetDirectoryName(pfi.name.ToLower().Replace('/', Path.DirectorySeparatorChar)).Replace(Path.DirectorySeparatorChar, '/');
                    var pos = folders.IndexOf(n);
                    if (pos >= 0)
                        continue;
                    folders.Add(n);
                }
                catch
                {
                }
            }
            if (sortTheList)
                folders.Sort();
        }

        /// <summary>
        /// Get a list of files inside a given "directory".
        /// </summary>
        /// <param name="dirname">Directory name to search in</param>
        /// <returns>Returns a new List of all found files</returns>
        public List<AAPakFileInfo> GetFilesInDirectory(string dirname)
        {
            var res = new List<AAPakFileInfo>();
            dirname = dirname.ToLower();
            foreach (AAPakFileInfo pfi in files)
            {
                // extract dir name
                string n = string.Empty;
                try
                {
                    n = Path.GetDirectoryName(pfi.name.ToLower().Replace('/', Path.DirectorySeparatorChar)).Replace(Path.DirectorySeparatorChar, '/');
                }
                catch
                {
                    n = string.Empty;
                }
                if (n == dirname)
                    res.Add(pfi);
            }
            return res;
        }

        /// <summary>
        /// Find a file information inside the pak by it's filename
        /// </summary>
        /// <param name="filename">filename inside the pak of the requested file</param>
        /// <param name="fileInfo">Returns the AAPakFile info of the requested file or nullAAPakFileInfo if it does not exist</param>
        /// <returns>Returns true if the file was found</returns>
        public bool GetFileByName(string filename, ref AAPakFileInfo fileInfo)
        {
            var fn = ToPakSlashes(filename);
            foreach (AAPakFileInfo pfi in files)
            {
                if (pfi.name == fn)
                {
                    fileInfo = pfi;
                    return true;
                }
            }
            fileInfo = nullAAPakFileInfo; // return null file if it fails
            return false;
        }

        public bool GetFileByIndex(int fileIndex, ref AAPakFileInfo fileInfo)
        {
            foreach (AAPakFileInfo pfi in files)
            {
                if (pfi.entryIndexNumber == fileIndex)
                {
                    fileInfo = pfi;
                    return true;
                }
            }
            fileInfo = nullAAPakFileInfo; // return null file if it fails
            return false;
        }

        public static string ToPakSlashes(string fileName)
        {
            return fileName.Replace(Path.DirectorySeparatorChar, '/');
        }
        
        /// <summary>
        /// Check if file exists within the pak
        /// </summary>
        /// <param name="filename">filename of the file to check</param>
        /// <returns>Returns true if the file was found</returns>
        public bool FileExists(string filename)
        {
            var fn = ToPakSlashes(filename);
            foreach (AAPakFileInfo pfi in files)
            {
                if (pfi.name == fn)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Exports a given file as a Stream
        /// </summary>
        /// <param name="file">AAPakFileInfo of the file to be exported</param>
        /// <returns>Returns a SubStream of file within the pak</returns>
        public Stream ExportFileAsStream(AAPakFileInfo file)
        {
            return new SubStream(_gpFileStream, file.offset, file.size);
        }

        /// <summary>
        /// Exports a given file as stream (might not be thread-safe)
        /// </summary>
        /// <param name="fileName">filename inside the pak of the file to be exported</param>
        /// <returns>Returns a SubStream of file within the pak</returns>
        public Stream ExportFileAsStream(string fileName)
        {
            AAPakFileInfo file = nullAAPakFileInfo ;
            if (GetFileByName(fileName, ref file) == true)
            {
                return new SubStream(_gpFileStream, file.offset, file.size);
            }
            else
            {
                return new MemoryStream();
            }
        }

        /// <summary>
        /// Exports a given file as stream by first creating a new file handle to access it
        /// </summary>
        /// <param name="fileName">filename inside the pak of the file to be exported</param>
        /// <returns>Returns a SubStream of file within the pak</returns>
        public Stream ExportFileAsStreamCloned(string fileName)
        {
            AAPakFileInfo file = nullAAPakFileInfo ;
            if (GetFileByName(fileName, ref file) == true)
            {
                var fs = new FileStream(_gpFilePath, FileMode.Open, FileAccess.Read);
                if (fs.Length > 0)
                    return new SubStream(fs, file.offset, file.size);
            }
            return new MemoryStream();
        }
        

        /// <summary>
        /// Calculates and set the MD5 Hash of a given file
        /// </summary>
        /// <param name="file">AAPakFileInfo of the file to be updated</param>
        /// <returns>Returns the new hash as a hex string (with removed dashes)</returns>
        public string UpdateMD5(AAPakFileInfo file)
        {
            MD5 hash = MD5.Create();
            var fs = ExportFileAsStream(file);
            var newHash = hash.ComputeHash(fs);
            hash.Dispose();
            if (!file.md5.SequenceEqual(newHash))
            {
                // Only update if different
                newHash.CopyTo(file.md5,0);
                isDirty = true;
            }
            return BitConverter.ToString(file.md5).Replace("-", ""); // Return the (updated) md5 as a string
        }

        /// <summary>
        /// Manually set a new MD5 value for a file
        /// </summary>
        /// <param name="file"></param>
        /// <param name="newHash"></param>
        /// <returns>Returns true if a new value was set</returns>
        public bool SetMD5(AAPakFileInfo file, byte[] newHash)
        {
            if ((file == null) || (newHash == null) || (newHash.Length != 16))
                return false;
            newHash.CopyTo(file.md5, 0);
            isDirty = true;
            return true;
        }


        /// <summary>
        /// Try to find a file inside the pakfile base on a offset position inside the pakfile.
        /// Note: this only checks inside the used files and does not account for "deleted" files
        /// </summary>
        /// <param name="offset">Offset to check against</param>
        /// <param name="fileInfo">Returns the found file's info, or nullAAPakFileInfo if nothing was found</param>
        /// <returns>Returns true if the location was found to be inside a valid file</returns>
        public bool FindFileByOffset(long offset, ref AAPakFileInfo fileInfo)
        {
            foreach(AAPakFileInfo pfi in files)
            {
                if ((offset >= pfi.offset) && (offset <= (pfi.offset + pfi.size + pfi.paddingSize)))
                {
                    fileInfo = pfi;
                    return true;
                }
            }
            fileInfo = nullAAPakFileInfo;
            return false;
        }

        /// <summary>
        /// Replaces a file's data with new data from a stream, can only be used if the current file location has enough space to hold the new data
        /// </summary>
        /// <param name="pfi">Fileinfo of the file to replace</param>
        /// <param name="sourceStream">Stream to replace the data with</param>
        /// <param name="modifyTime">Time to be used as a modified time stamp</param>
        /// <returns>Returns true on success</returns>
        public bool ReplaceFile(ref AAPakFileInfo pfi, Stream sourceStream, DateTime modifyTime)
        {
            // Overwrite a existing file in the pak

            if (readOnly)
                return false;

            // Fail if the new file is too big
            if (sourceStream.Length > (pfi.size + pfi.paddingSize))
                return false;

            // Save endpos for easy calculation later
            long endPos = pfi.offset + pfi.size + pfi.paddingSize;

            try
            {
                // Copy new data over the old data
                _gpFileStream.Position = pfi.offset;
                sourceStream.Position = 0;
                sourceStream.CopyTo(_gpFileStream);
            }
            catch
            {
                return false;
            }

            // Update File Size in File Table
            pfi.size = sourceStream.Length;
            pfi.sizeDuplicate = pfi.size ;
            // Calculate new Padding size
            pfi.paddingSize = (int)(endPos - pfi.size - pfi.offset);
            // Recalculate the MD5 hash
            UpdateMD5(pfi); // TODO: optimize this to calculate WHILE we are copying the stream
            pfi.modifyTime = modifyTime.ToFileTimeUtc();

            if (PakType == PakFileType.TypeB)
                pfi.dummy1 = 0x80000000;

            // Mark File Table as dirty
            isDirty = true;

            return true;
        }

        /// <summary>
        /// Delete a file from pak. Behaves differenly depending on the paddingDeleteMode setting
        /// </summary>
        /// <param name="pfi">AAPakFileInfo of the file that is to be deleted</param>
        /// <returns>Returns true on success</returns>
        public bool DeleteFile(AAPakFileInfo pfi)
        {
            // When we detele a file from the pak, we remove the entry from the FileTable and expand the previous file's padding to take up the space
            if (readOnly)
                return false;

            if (paddingDeleteMode)
            {
                AAPakFileInfo prevPfi = nullAAPakFileInfo;
                if (FindFileByOffset(pfi.offset - 1, ref prevPfi))
                {
                    // If we have a previous file, expand it's padding area with the free space from this file
                    prevPfi.paddingSize += (int)pfi.size + pfi.paddingSize;
                }
                files.Remove(pfi);
            }
            else
            {
                // "move" offset and size data to extrafiles
                AAPakFileInfo eFile = new AAPakFileInfo();
                eFile.name = "__unused__";
                eFile.offset = pfi.offset;
                eFile.size = pfi.size + pfi.paddingSize;
                eFile.sizeDuplicate = eFile.size;
                eFile.paddingSize = 0 ;
                eFile.md5 = new byte[16];
                if (PakType == PakFileType.TypeB)
                    eFile.dummy1 = 0x80000000;

                extraFiles.Add(eFile);

                files.Remove(pfi);
            }
            isDirty = true;
            return true;
        }

        /// <summary>
        /// Delete a file from pak. Behaves differenly depending on the paddingDeleteMode setting
        /// </summary>
        /// <param name="filename">Filename of the file to delete from the pakfile</param>
        /// <returns>Returns true on success or if the file didn't exist</returns>
        public bool DeleteFile(string filename)
        {
            if (readOnly)
                return false;

            AAPakFileInfo pfi = nullAAPakFileInfo;
            if (GetFileByName(filename, ref pfi))
            {
                return DeleteFile(pfi);
            }
            else
            {
                // Return true if the file didn't exist
                return true;
            }
        }

        /// <summary>
        /// Adds a new file into the pak
        /// </summary>
        /// <param name="filename">Filename of the file inside the pakfile</param>
        /// <param name="sourceStream">Source Stream containing the file data</param>
        /// <param name="CreateTime">Time to use as initial file creation timestamp</param>
        /// <param name="ModifyTime">Time to use as last modified timestamp</param>
        /// <param name="autoSpareSpace">When set, tries to pre-allocate extra free space at the end of the file, this will be 25% of the filesize if used. If a "deleted file" is used, this parameter is ignored</param>
        /// <param name="pfi">Returns the fileinfo of the newly created file</param>
        /// <returns>Returns true on success</returns>
        public bool AddAsNewFile(string filename, Stream sourceStream, DateTime CreateTime, DateTime ModifyTime, bool autoSpareSpace, out AAPakFileInfo pfi)
        {
            // When we have a new file, or previous space wasn't enough, we will add it where the file table starts, and move the file table
            if (readOnly)
            {
                pfi = nullAAPakFileInfo;
                return false;
            }
            bool addedAtTheEnd = true;

            AAPakFileInfo newFile = new AAPakFileInfo();
            newFile.name = filename;
            newFile.offset = _header.FirstFileInfoOffset;
            newFile.size = sourceStream.Length;
            newFile.sizeDuplicate = newFile.size;
            newFile.createTime = CreateTime.ToFileTimeUtc();
            newFile.modifyTime = ModifyTime.ToFileTimeUtc();
            newFile.paddingSize = 0;
            newFile.md5 = new byte[16];
            if (PakType == PakFileType.TypeB)
                newFile.dummy1 = 0x80000000;

            // check if we have "unused" space in extraFiles that we can use
            for(int i = 0; i < extraFiles.Count;i++)
            {
                if (newFile.size <= extraFiles[i].size)
                {
                    // Copy the spare file's properties and remove it from extraFiles
                    newFile.offset = extraFiles[i].offset;
                    newFile.paddingSize = (int)(extraFiles[i].size - newFile.size); // This should already be aligned
                    addedAtTheEnd = false;
                    extraFiles.Remove(extraFiles[i]);
                    break;
                }
            }

            if (addedAtTheEnd)
            {
                // Only need to calculate padding if we are adding at the end
                var dif = (newFile.size % 0x200);
                if (dif > 0)
                {
                    newFile.paddingSize = (int)(0x200 - dif);
                }
                if (autoSpareSpace)
                {
                    // If autoSpareSpace is used to add files, we will reserve some extra space as padding
                    // Add 25% by default
                    var spareSpace = (newFile.size / 4);
                    spareSpace -= (spareSpace % 0x200); // Align the spare space
                    newFile.paddingSize += (int)spareSpace;
                }
            }

            // Add to files list
            files.Add(newFile);

            isDirty = true;

            // Add File Data
            _gpFileStream.Position = newFile.offset;
            sourceStream.Position = 0;
            sourceStream.CopyTo(_gpFileStream);

            if (addedAtTheEnd)
            {
                _header.FirstFileInfoOffset = newFile.offset + newFile.size + newFile.paddingSize;
            }

            UpdateMD5(newFile); // TODO: optimize this to calculate WHILE we are copying the stream

            // Set output
            pfi = newFile;
            return true;
        }

        /// <summary>
        /// Adds or replace a given file with name filename with data from sourceStream
        /// </summary>
        /// <param name="filename">The filename used inside the pak</param>
        /// <param name="sourceStream">Source Stream of file to be added</param>
        /// <param name="CreateTime">Time to use as original file creation time</param>
        /// <param name="ModifyTime">Time to use as last modified time</param>
        /// <param name="autoSpareSpace">Enable adding 25% of the sourceStream size as padding when not replacing a file</param>
        /// <param name="pfi">AAPakFileInfo of the newly added or modified file</param>
        /// <returns>Returns true on success</returns>
        public bool AddFileFromStream(string filename, Stream sourceStream, DateTime CreateTime, DateTime ModifyTime, bool autoSpareSpace, out AAPakFileInfo pfi)
        {
            pfi = nullAAPakFileInfo;
            if (readOnly)
            {
                return false;
            }

            bool addAsNew = true;
            // Try to find the existing file
            if (GetFileByName(filename, ref pfi))
            {
                var reservedSizeMax = pfi.size + pfi.paddingSize;
                addAsNew = (sourceStream.Length > reservedSizeMax);
                // Bugfix: If we have inssuficient space, make sure to delete the old file first as well
                if (addAsNew)
                {
                    DeleteFile(pfi);
                }
            }

            if (addAsNew)
            {
                return AddAsNewFile(filename, sourceStream, CreateTime, ModifyTime, autoSpareSpace, out pfi);
            }
            else
            {
                return ReplaceFile(ref pfi, sourceStream, ModifyTime);
            }
        }

        /// <summary>
        /// Adds a file into the pakfile with a given name
        /// </summary>
        /// <param name="sourceFileName">Filename of the source file to be added</param>
        /// <param name="asFileName">Filename inside the pakfile to use</param>
        /// <param name="autoSpareSpace">When set, tries to pre-allocate extra free space at the end of the file, this will be 25% of the filesize if used. If a "deleted file" is used, this parameter is ignored</param>
        /// <returns>Returns true on success</returns>
        public bool AddFileFromFile(string sourceFileName, string asFileName, bool autoSpareSpace)
        {
            if (!File.Exists(sourceFileName))
                return false;
            var createTime = File.GetCreationTime(sourceFileName);
            var modTime = File.GetLastWriteTime(sourceFileName);
            var fs = File.OpenRead(sourceFileName);
            var res = AddFileFromStream(asFileName, fs, createTime, modTime, autoSpareSpace, out _);
            fs.Dispose();
            return res;
        }

        /// <summary>
        /// Convert a stream into a string
        /// </summary>
        /// <param name="stream">Source stream</param>
        /// <returns>String value of the data isnide the stream</returns>
        static public string StreamToString(Stream stream)
        {
            stream.Position = 0;
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Convert a string into a MemoryStream
        /// </summary>
        /// <param name="src">Source string</param>
        /// <returns>A new MemoryStream that holds the source string's data</returns>
        static public Stream StringToStream(string src)
        {
            byte[] byteArray = Encoding.UTF8.GetBytes(src);
            return new MemoryStream(byteArray);
        }

    }
}

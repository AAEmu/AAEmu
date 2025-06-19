using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AAEmu.Commons.Exceptions;

// 来源：https://github.com/ZeromusXYZ/AAEmu-Packer

namespace AAEmu.Commons.Utils.AAPak;

/// <summary>
/// 文件详情块
/// </summary>
public class AAPakFileInfo
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x108)] public string name;
    public long offset;
    public long size;
    public long sizeDuplicate; // 可能是压缩数据大小？如果使用，观察到的始终与 size 相同
    public int paddingSize; // 直到下一个 512 字节块大小的剩余可用空间字节数（或直到下一个文件的空间）
    public byte[] md5; // 这里应该是 16 字节
    public uint dummy1; // 看起来像填充，通常观察到为 0 或 0x80000000，可能是文件标志？
    public long createTime;
    public long modifyTime;
    public ulong dummy2; // 看起来像用于填充块的填充，观察到为 0
    // 以下内容不是结构的一部分，但由程序使用
    public int entryIndexNumber = -1;
    public int deletedIndexNumber = -1;
}

/// <summary>
/// Pak 包头信息
/// </summary>
public class AAPakFileHeader
{
    /// <summary>
    /// XLGames 用于 ArcheAge 的默认 AES128 密钥，用作头部和文件信息数据的加密密钥
    /// 32 1F 2A EE AA 58 4A B4 9A 6C 9E 09 D5 9E 9C 6F
    /// </summary>
    private readonly byte[] XLGamesKey = new byte[] { 0x32, 0x1F, 0x2A, 0xEE, 0xAA, 0x58, 0x4A, 0xB4, 0x9A, 0x6C, 0x9E, 0x09, 0xD5, 0x9E, 0x9C, 0x6F };
    /// <summary>
    /// 当前加密密钥
    /// </summary>
    private byte[] key;
    protected static readonly int headerSize = 0x200;
    protected static readonly int fileInfoSize = 0x150;
    /// <summary>
    /// 包含加密文件信息和文件头部分的内存流
    /// </summary>
    public MemoryStream FAT = new();

    public AAPak _owner;
    public int Size = headerSize;
    public long FirstFileInfoOffset;
    public long AddFileOffset;
    public byte[] rawData = new byte[headerSize]; // 未加密的头部
    public byte[] data = new byte[headerSize]; // 已解密的头部数据
    public bool isValid;
    /// <summary>
    /// 此 pak 包中已使用文件的数量
    /// </summary>
    public uint fileCount;
    /// <summary>
    /// 此 pak 包中未使用（“已删除”）文件的数量
    /// </summary>
    public uint extraFileCount;

    /// <summary>
    /// 用于比较的空 MD5 哈希值
    /// </summary>
    // 未使用
    //public static byte[] nullHash = new byte[16];

    /// <summary>
    /// 用于比较的空 MD5 哈希值的十六进制字符串形式
    /// </summary>
    //public static string nullHashString = "".PadRight(32, '0');
    public static string LastAESError { get; set; } = string.Empty;

    /// <summary>
    /// 为 Pak 文件创建一个新的头部块
    /// </summary>
    /// <param name="owner">此头部所属的 AAPak</param>
    public AAPakFileHeader(AAPak owner)
    {
        _owner = owner;
        SetCustomKey(XLGamesKey);
    }

    /* 空的析构函数
    ~AAPakFileHeader()
    {
        // FAT.Dispose();
    }*/

    /// <summary>
    /// 如果要在 pak 文件上使用自定义密钥，请使用此函数更改用于 FAT 和头部数据加密/解密的密钥
    /// </summary>
    /// <param name="newKey"></param>
    public void SetCustomKey(byte[] newKey)
    {
        key = new byte[newKey.Length];
        newKey.CopyTo(key, 0);
    }

    /// <summary>
    /// 恢复到原始加密密钥，关闭文件时也会自动调用此函数
    /// </summary>
    public void SetDefaultKey()
    {
        XLGamesKey.CopyTo(key, 0);
    }

    /// <summary>
    /// 使用 AES128 CBC 加密或解密字节数组 -
    /// 源代码：https://stackoverflow.com/questions/44782910/aes128-decryption-in-c-sharp
    /// </summary>
    /// <param name="message">要处理的字节数组</param>
    /// <param name="key">要使用的加密密钥</param>
    /// <param name="doEncryption">False = 解密, True = 加密</param>
    /// <returns>返回包含已处理数据的新字节数组</returns>
    public static byte[] EncryptAES(byte[] message, byte[] key, bool doEncryption)
    {
        try
        {
            using Aes aes = Aes.Create();
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
            using Aes aes = Aes.Create();
            aes.Key = key;
            aes.IV = new byte[16];
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            ICryptoTransform cipher;

            if (doEncryption)
                cipher = aes.CreateEncryptor();
            else
                cipher = aes.CreateDecryptor();

            // 创建用于加密的流。

            using CryptoStream csEncrypt = new CryptoStream(target, cipher, CryptoStreamMode.Write);
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
            using Aes aes = Aes.Create();
            aes.Key = key;
            aes.IV = customIV;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;

            ICryptoTransform cipher;

            if (doEncryption)
                cipher = aes.CreateEncryptor();
            else
                cipher = aes.CreateDecryptor();

            // 创建用于加密的流。
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
    /// 与 EncryptAES 相同，但指定特定的 IV (初始化向量)
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
            using Aes aes = Aes.Create();
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
    /// 定位并加载加密的 FAT 数据到内存中
    /// </summary>
    /// <returns>成功则返回 true</returns>
    public bool LoadRawFAT()
    {
        // 将所有文件表数据读入内存
        FAT.SetLength(0);

        long TotalFileInfoSize = (fileCount + extraFileCount) * fileInfoSize;
        _owner._gpFileStream.Seek(0, SeekOrigin.End);
        FirstFileInfoOffset = _owner._gpFileStream.Position;

        // 搜索第一个文件位置，它需要与 0x200 大小的块对齐
        FirstFileInfoOffset -= headerSize;
        FirstFileInfoOffset -= TotalFileInfoSize;
        var dif = FirstFileInfoOffset % 0x200;
        // 与前一个 512 字节的块对齐
        FirstFileInfoOffset -= dif;

        _owner._gpFileStream.Position = FirstFileInfoOffset;

        using SubStream _fat = new SubStream(_owner._gpFileStream, FirstFileInfoOffset, _owner._gpFileStream.Length - FirstFileInfoOffset);
        _fat.CopyTo(FAT);

        return true;
    }

    /// <summary>
    /// 将当前文件信息写回 FAT（加密）
    /// </summary>
    /// <returns>成功则返回 true</returns>
    public bool WriteToFAT()
    {
        if (_owner.PakType == PakFileType.CSV)
            return false;

        // 将所有文件表数据读入内存
        FAT.SetLength(0);

        int bufSize = 0x150; // Marshal.SizeOf(typeof(AAPakFileInfo)); // 获取 AAPakFileInfo 类型的大小
        MemoryStream ms = new MemoryStream(bufSize); // 可能不需要中间的内存流，但这样处理起来更容易
        using BinaryWriter writer = new BinaryWriter(ms);

        // 初始化文件计数
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
                // TypeA 类型的文件在前，额外文件在后
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
                    // 如果执行到这里，说明你的电脑计算出了问题
                    pfi = null;
                    break;
                }
            }
            else
            if (_owner.PakType == PakFileType.TypeB)
            {
                // TypeB 类型的额外文件在前，普通文件在后
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
                    // 如果执行到这里，说明你的电脑计算出了问题
                    pfi = null;
                    break;
                }
            }
            else
            {
                // 不知何故出现了不支持的类型
                throw new GameException("Don't know how to write this FAT: " + _owner.PakType);
            }

            if (_owner.PakType == PakFileType.TypeA)
            {
                // 手动写入文件名字符串
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

                // 手动写入文件名字符串
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
                // 手动写入文件名字符串
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
                writer.Write(pfi.modifyTime); // 对于 TypeF 类型，这通常为零
            }
            else
            {
                throw new GameException("I don't know how to write this file format: " + _owner.PakType);
            }

            // 加密新的文件数据并将其写入 FAT 内存流
            byte[] decryptedFileData = new byte[bufSize];
            ms.Position = 0;
            ms.Read(decryptedFileData, 0, bufSize);
            byte[] rawFileData = EncryptAES(decryptedFileData, key, true); // encrypt header data
            FAT.Write(rawFileData, 0, bufSize);
        }
        ms.Dispose();

        // 计算到头部的填充
        var dif = (FAT.Length % 0x200);
        if (dif > 0)
        {
            var pad = (0x200 - dif);
            FAT.SetLength(FAT.Length + pad);
            FAT.Position = FAT.Length;
        }
        // 更新头部信息
        fileCount = (uint)_owner.files.Count;
        extraFileCount = (uint)_owner.extraFiles.Count;
        // 为头部扩展大小
        FAT.SetLength(FAT.Length + headerSize);
        // 加密头部数据
        EncryptHeaderData();
        // 写入加密的头部
        FAT.Write(rawData, 0, 0x20);

        return true;
    }

    /// <summary>
    /// 读取并解密已加载到 FAT MemoryStream 中的文件详情表
    /// </summary>
    public void ReadFileTable()
    {
        // 请参阅 aa.bms QuickBMS 文件以供参考
        FAT.Position = 0;

        int bufSize = 0x150; // Marshal.SizeOf(typeof(AAPakFileInfo)); // 获取 AAPakFileInfo 类型的大小
        MemoryStream ms = new MemoryStream(bufSize); // 可能不需要中间的内存流，但这样处理起来更容易
        using BinaryReader reader = new BinaryReader(ms);

        // 读取文件
        _owner.files.Clear();
        _owner.extraFiles.Clear();
        var totalFileCount = fileCount + extraFileCount;
        var filesToGo = fileCount;
        var extraToGo = extraFileCount;
        var fileIndexCounter = -1;
        var deletedIndexCounter = -1;
        for (uint i = 0; i < totalFileCount; i++)
        {
            // 读取并解密文件信息块
            byte[] rawFileData = new byte[bufSize]; // 已解密的文件数据
            FAT.Read(rawFileData, 0, bufSize);
            byte[] decryptedFileData = EncryptAES(rawFileData, key, false);

            // 将解密后的数据读入 AAPakFileInfo
            ms.SetLength(0);
            ms.Write(decryptedFileData, 0, bufSize);
            ms.Position = 0;
            AAPakFileInfo pfi = new AAPakFileInfo();
            if (_owner.PakType == PakFileType.TypeA)
            {
                // 手动写入文件名字符串
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
                pfi.dummy1 = reader.ReadUInt32(); // 观察到为 0x00000000
                pfi.createTime = reader.ReadInt64();
                pfi.modifyTime = reader.ReadInt64();
                pfi.dummy2 = reader.ReadUInt64(); // 未使用？
            }
            else
            if (_owner.PakType == PakFileType.TypeB)
            {
                pfi.paddingSize = reader.ReadInt32();
                pfi.md5 = reader.ReadBytes(16);
                pfi.dummy1 = reader.ReadUInt32(); // 0x80000000
                pfi.size = reader.ReadInt64();
                // 手动写入文件名字符串
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
                pfi.dummy2 = reader.ReadUInt64(); // 未使用？
            }
            else
            if (_owner.PakType == PakFileType.TypeF)
            {
                pfi.dummy2 = reader.ReadUInt64(); // 未使用？
                // 手动写入文件名字符串
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
                pfi.dummy1 = reader.ReadUInt32(); // 观察到为 0x00000000
                pfi.createTime = reader.ReadInt64();
                pfi.modifyTime = reader.ReadInt64(); // 对于 TypeF 类型，这通常为零
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
                // TypeA 类型的文件在前，额外文件在后
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
                    // “额外”文件。看起来这些是重命名为 "__unused__" 的旧的已删除文件
                    // 这些文件可能还有更多含义，但目前无法确定，在我的 pak 包中它们看起来是 512 字节的块
                    deletedIndexCounter++;
                    pfi.deletedIndexNumber = deletedIndexCounter;

                    extraToGo--;
                    _owner.extraFiles.Add(pfi);
                }
            }
            else
            if (_owner.PakType == PakFileType.TypeB)
            {
                // TypeB 类型的额外文件在前，普通文件在后
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
                // 快报警，非法类型正在侵入我们的安全空间
            }

            /*
            // 调试内容
            if (pfi.name == "bin32/archeage.exe")
            {
                ByteArrayToHexFile(decryptedFileData, "file-"+ i.ToString() + ".hex");
                File.WriteAllBytes("file-" + i.ToString() + ".bin",decryptedFileData);
            }
            */

            // 如果需要，更新我们的“文件数据结束”位置
            if ((pfi.offset + pfi.size + pfi.paddingSize) > AddFileOffset)
            {
                AddFileOffset = pfi.offset + pfi.size + pfi.paddingSize;
            }
        }

        ms.Dispose();
    }


    /// <summary>
    /// 调试辅助函数，将字节数组写入十六进制文本文件
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="fileName"></param>
    /* 未使用的方法
    private static void ByteArrayToHexFile(byte[] bytes, string fileName)
    {
        string s = "";
        for (int i = 0; i < bytes.Length; i++)
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
    }*/

    /// <summary>
    /// 调试辅助函数，将字节数组转换为十六进制字符串
    /// </summary>
    /// <param name="bytes"></param>
    public static string ByteArrayToHexString(byte[] bytes, string spacingText = " ", string lineFeed = "\r\n")
    {
        string s = "";
        for (int i = 0; i < bytes.Length; i++)
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
    /// 解密当前头部数据以获取文件计数
    /// </summary>
    public void DecryptHeaderData()
    {
        data = EncryptAES(rawData, key, false);

        // 通过其标识符检查有效的头部/尾部
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
            // 看起来这不是一个 pak 文件，文件已损坏，或者格式未知
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
    /// 加密当前头部数据
    /// </summary>
    public void EncryptHeaderData()
    {
        MemoryStream ms = new MemoryStream();
        ms.Write(data, 0, headerSize);
        ms.Position = 0;
        using BinaryWriter writer = new BinaryWriter(ms);

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
            // 我不知道如何处理不应该存在的东西
        }

        ms.Position = 0;
        ms.Read(data, 0, headerSize);
        ms.Dispose();
        // 将我们存储的数据加密到 rawData
        rawData = EncryptAES(data, key, true);
    }
}

public enum PakFileType { TypeA, TypeB, CSV, TypeF };

/// <summary>
/// AAPak 类，用于处理 ArcheAge 的 game_pak 文件
/// </summary>
public class AAPak
{
    /// <summary>
    /// 作为文件详情的空值返回的虚拟数据，可用于初始化要作为 ref 传递的变量
    /// </summary>
    public AAPakFileInfo nullAAPakFileInfo = new();
    public string _gpFilePath { get; private set; }
    public FileStream _gpFileStream { get; private set; }
    /// <summary>
    /// 指向此 pak 文件的头部
    /// </summary>
    public AAPakFileHeader _header;
    /// <summary>
    /// 检查当前 pak 文件信息是否已加载到内存中
    /// </summary>
    public bool isOpen = false;
    /// <summary>
    /// 如果已进行需要重写 FAT 和/或头部的更改，则设置为 true
    /// </summary>
    public bool isDirty = false;
    /// <summary>
    /// 如果这不是一个 pak 文件，而是从其他地方加载的信息，则设置为 true
    /// </summary>
    public bool isVirtual = false;
    /// <summary>
    /// 所有已使用文件的列表
    /// </summary>
    public List<AAPakFileInfo> files = new();
    /// <summary>
    /// 所有未使用文件的列表，通常这些文件都命名为 "__unused__"
    /// </summary>
    public List<AAPakFileInfo> extraFiles = new();
    /// <summary>
    /// 所有文件夹名称的虚拟列表，使用 GenerateFolderList() 填充此列表（可能需要一段时间）
    /// </summary>
    public List<string> folders = new();
    /// <summary>
    /// 显示此 pak 文件是否以只读模式打开
    /// </summary>
    public bool readOnly { get; private set; }
    /// <summary>
    /// 如果设置为 true，则将删除操作释放的空间添加到前一个文件的填充中。
    /// 如果为 false（默认），则将文件“移动”到 extraFiles 以释放空间，从而允许重用该空间。
    /// 仅当您正在编写自己的专用修补程序并且仅在特殊情况下才需要更改此设置
    /// </summary>
    public bool paddingDeleteMode = false;
    public PakFileType PakType = PakFileType.TypeA;
    public bool DebugMode = false;

    /// <summary>
    /// 创建和/或打开 game_pak 文件
    /// </summary>
    /// <param name="filePath">pak 的文件名</param>
    /// <param name="openAsReadOnly">如果为 true，则以只读模式打开 pak。如果设置了 createAsNewPak，则忽略此参数</param>
    /// <param name="createAsNewPak">如果为 true，则忽略 openAsReadOnly，并将在 filePath 位置以读/写模式创建一个新的 pak。警告：这将覆盖该位置的任何现有 pak！</param>
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
                    // 以 CSV 数据形式打开文件
                    isLoaded = OpenVirtualCSVPak(filePath);
                    return;
                }
                // 仅当设置为只读（且不是新文件）时，才允许以 CSV 文件形式打开
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
    /// 打开一个 pak 文件，仅当当前没有加载其他文件时才能使用
    /// </summary>
    /// <param name="filePath">要打开的 pak 文件的文件名</param>
    /// <param name="openAsReadOnly">设置为 true 以只读模式打开 pak</param>
    /// <returns>成功则返回 true，否则返回 false</returns>
    public bool OpenPak(string filePath, bool openAsReadOnly)
    {
        // 如果已打开则失败
        if (isOpen)
            return false;

        // 检查是否存在
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
            // 以 CSV 数据形式打开文件
            return OpenVirtualCSVPak(filePath);
        }

        try
        {
            // 打开流
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
            _gpFilePath = null;
            isOpen = false;
            readOnly = true;
            return false;
        }
    }

    /// <summary>
    /// 创建一个名为 filename 的新 pak 文件，如果存在同名文件则会覆盖
    /// </summary>
    /// <param name="filePath">新 pak 文件的文件名</param>
    /// <returns>成功则返回 true，如果出现问题或仍有 pak 文件打开则返回 false</returns>
    public bool NewPak(string filePath)
    {
        // 如果已打开则失败
        if (isOpen)
            return false;
        isVirtual = false;
        try
        {
            // 创建新的文件流
            _gpFileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
            _gpFilePath = filePath;
            readOnly = false;
            isOpen = true;
            isDirty = true;
            SaveHeader(); // 保存空白数据
            return ReadHeader(); // 读取空白数据以确认
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
        // 如果已打开则失败
        if (isOpen)
            return false;

        // 检查是否存在
        if (!File.Exists(csvfilePath))
        {
            return false;
        }
        isVirtual = true;
        _gpFileStream = null; // 未在虚拟 pak 上使用
        try
        {
            // 打开流
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
    /// 关闭当前打开的 pak 文件（如果已打开）
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
    /// 加密并将头部和文件信息表保存回 pak。
    /// 如果进行了更改，则在 ClosePak() 时也会自动调用此方法。
    /// 警告：如果添加或删除了文件，保存失败可能会损坏您的 pak！
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
    /// 读取 Pak 头部和 FAT
    /// </summary>
    /// <returns>如果读取的信息构成有效的 pak 文件，则返回 true</returns>
    protected bool ReadHeader()
    {
        files.Clear();
        extraFiles.Clear();
        folders.Clear();

        // 将最后 512 字节作为原始头部数据读取
        _gpFileStream.Seek(-_header.Size, SeekOrigin.End);

        // 将正确位置标记为头部偏移位置
        _gpFileStream.Read(_header.rawData, 0, _header.Size); // 我们不需要读取整个内容，只需前 32 个字节包含数据即可
        // _gpFileStream.Read(_header.rawData, 0, _header.Size);

        _header.DecryptHeaderData();

        if (_header.isValid)
        {
            // 仅允许编辑 TypeA 类型
            // if (PakType != PakFileType.PakTypeA) readOnly = true;
            _header.LoadRawFAT();
            _header.ReadFileTable();
        }
        else
        {
            _header.FAT.SetLength(0);
        }

        return _header.isValid;
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
    /// 从给定的专用字符串创建文件时间
    /// </summary>
    /// <param name="encodedString"></param>
    /// <returns>FILETIME (UTC)</returns>
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
            if (!int.TryParse(encodedString.AsSpan(0, 4), out yyyy)) yyyy = 0;
            if (!int.TryParse(encodedString.AsSpan(5, 2), out mm)) mm = 0;
            if (!int.TryParse(encodedString.AsSpan(8, 2), out dd)) dd = 0;
            if (!int.TryParse(encodedString.AsSpan(11, 2), out hh)) hh = 0;
            if (!int.TryParse(encodedString.AsSpan(14, 2), out nn)) nn = 0;
            if (!int.TryParse(encodedString.AsSpan(17, 2), out ss)) ss = 0;

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
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var fields = line.Split(';');
                if (fields.Length == 10)
                {
                    try
                    {
                        var fni = new AAPakFileInfo();

                        // 看起来有效，读取它
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

                        // TODO：检查此读取是否正确
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
    /// 使用从 pak 内部找到的文件派生的虚拟文件夹名称填充文件夹字符串列表
    /// </summary>
    /// <param name="sortTheList">如果您不希望对生成的文件夹列表进行排序，则设置为 false（不推荐）</param>
    public void GenerateFolderList(bool sortTheList = true)
    {
        // pak 文件中没有存储实际的目录信息，因此我们仅根据文件名生成它
        folders.Clear();
        if (!isOpen || !_header.isValid) return;
        foreach (AAPakFileInfo pfi in files)
        {
            if (pfi.name == string.Empty)
                continue;
            try
            {
                // 我知道这是个恐怖的函数 :p
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
    /// 获取给定“目录”内的文件列表。
    /// </summary>
    /// <param name="dirname">要搜索的目录名</param>
    /// <returns>返回包含所有找到文件的新列表</returns>
    public List<AAPakFileInfo> GetFilesInDirectory(string dirname)
    {
        var res = new List<AAPakFileInfo>();
        dirname = dirname.ToLower();
        foreach (AAPakFileInfo pfi in files)
        {
            // 提取目录名
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
    /// 通过文件名在 pak 中查找文件信息
    /// </summary>
    /// <param name="filename">请求文件在 pak 中的文件名</param>
    /// <param name="fileInfo">返回请求文件的 AAPakFile 信息，如果不存在则返回 nullAAPakFileInfo</param>
    /// <returns>如果找到文件则返回 true</returns>
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
        fileInfo = nullAAPakFileInfo; // 如果失败则返回空文件
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
        fileInfo = nullAAPakFileInfo; // 如果失败则返回空文件
        return false;
    }

    public static string ToPakSlashes(string fileName)
    {
        return fileName.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// 检查文件是否存在于 pak 中
    /// </summary>
    /// <param name="filename">要检查的文件名</param>
    /// <returns>如果找到文件则返回 true</returns>
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
    /// 将给定文件导出为流
    /// </summary>
    /// <param name="file">要导出的文件的 AAPakFileInfo</param>
    /// <returns>返回 pak 中文件的 SubStream</returns>
    public Stream ExportFileAsStream(AAPakFileInfo file)
    {
        return new SubStream(_gpFileStream, file.offset, file.size);
    }

    /// <summary>
    /// 将给定文件导出为流（可能不是线程安全的）
    /// </summary>
    /// <param name="fileName">要导出的文件在 pak 中的文件名</param>
    /// <returns>返回 pak 中文件的 SubStream</returns>
    public Stream ExportFileAsStream(string fileName)
    {
        AAPakFileInfo file = nullAAPakFileInfo;
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
    /// 通过首先创建新的文件句柄来访问给定文件并将其导出为流
    /// </summary>
    /// <param name="fileName">要导出的文件在 pak 中的文件名</param>
    /// <returns>返回 pak 中文件的 SubStream</returns>
    public Stream ExportFileAsStreamCloned(string fileName)
    {
        AAPakFileInfo file = nullAAPakFileInfo;
        if (GetFileByName(fileName, ref file) == true)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            var fs = new FileStream(_gpFilePath, FileMode.Open, FileAccess.Read);
#pragma warning restore CA2000 // Dispose objects before losing scope
            if (fs.Length > 0)
                return new SubStream(fs, file.offset, file.size);
        }
        return new MemoryStream();
    }


    /// <summary>
    /// 计算并设置给定文件的 MD5 哈希值
    /// </summary>
    /// <param name="file">要更新的文件的 AAPakFileInfo</param>
    /// <returns>以十六进制字符串形式返回新的哈希值（已删除破折号）</returns>
    public string UpdateMD5(AAPakFileInfo file)
    {
        MD5 hash = MD5.Create();
        using var fs = ExportFileAsStream(file);
        var newHash = hash.ComputeHash(fs);
        hash.Dispose();
        if (!file.md5.SequenceEqual(newHash))
        {
            // 仅当不同时才更新
            newHash.CopyTo(file.md5, 0);
            isDirty = true;
        }
        return BitConverter.ToString(file.md5).Replace("-", ""); // 以字符串形式返回（更新后的）md5 值
    }

    /// <summary>
    /// 手动设置文件的新 MD5 值
    /// </summary>
    /// <param name="file"></param>
    /// <param name="newHash"></param>
    /// <returns>如果设置了新值则返回 true</returns>
    public bool SetMD5(AAPakFileInfo file, byte[] newHash)
    {
        if ((file == null) || (newHash == null) || (newHash.Length != 16))
            return false;
        newHash.CopyTo(file.md5, 0);
        isDirty = true;
        return true;
    }


    /// <summary>
    /// 尝试根据 pak 文件内的偏移位置在 pak 文件内查找文件。
    /// 注意：这仅检查已使用的文件，不包括“已删除”的文件
    /// </summary>
    /// <param name="offset">要检查的偏移量</param>
    /// <param name="fileInfo">返回找到文件的信息，如果未找到则返回 nullAAPakFileInfo</param>
    /// <returns>如果位置位于有效文件内则返回 true</returns>
    public bool FindFileByOffset(long offset, ref AAPakFileInfo fileInfo)
    {
        foreach (AAPakFileInfo pfi in files)
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
    /// 用来自流的新数据替换文件数据，仅当当前文件位置有足够空间容纳新数据时才能使用
    /// </summary>
    /// <param name="pfi">要替换的文件的 Fileinfo</param>
    /// <param name="sourceStream">用于替换数据的流</param>
    /// <param name="modifyTime">用作修改时间戳的时间</param>
    /// <returns>成功则返回 true</returns>
    public bool ReplaceFile(ref AAPakFileInfo pfi, Stream sourceStream, DateTime modifyTime)
    {
        // 覆盖 pak 中的现有文件

        if (readOnly)
            return false;

        // 如果新文件太大则失败
        if (sourceStream.Length > (pfi.size + pfi.paddingSize))
            return false;

        // 保存结束位置以便稍后计算
        long endPos = pfi.offset + pfi.size + pfi.paddingSize;

        try
        {
            // 将新数据复制到旧数据之上
            _gpFileStream.Position = pfi.offset;
            sourceStream.Position = 0;
            sourceStream.CopyTo(_gpFileStream);
        }
        catch
        {
            return false;
        }

        // 更新文件表中的文件大小
        pfi.size = sourceStream.Length;
        pfi.sizeDuplicate = pfi.size;
        // 计算新的填充大小
        pfi.paddingSize = (int)(endPos - pfi.size - pfi.offset);
        // 重新计算 MD5 哈希值
        UpdateMD5(pfi); // TODO：优化此项，以便在复制流的同时进行计算
        pfi.modifyTime = modifyTime.ToFileTimeUtc();

        if (PakType == PakFileType.TypeB)
            pfi.dummy1 = 0x80000000;

        // 将文件表标记为脏数据
        isDirty = true;

        return true;
    }

    /// <summary>
    /// 从 pak 中删除文件。根据 paddingDeleteMode 设置的不同，行为会有所不同
    /// </summary>
    /// <param name="pfi">要删除的文件的 AAPakFileInfo</param>
    /// <returns>成功则返回 true</returns>
    public bool DeleteFile(AAPakFileInfo pfi)
    {
        // 当我们从 pak 中删除文件时，我们会从文件表中删除该条目，并扩展前一个文件的填充以占用该空间
        if (readOnly)
            return false;

        if (paddingDeleteMode)
        {
            AAPakFileInfo prevPfi = nullAAPakFileInfo;
            if (FindFileByOffset(pfi.offset - 1, ref prevPfi))
            {
                // 如果存在前一个文件，则用此文件的可用空间扩展其填充区域
                prevPfi.paddingSize += (int)pfi.size + pfi.paddingSize;
            }
            files.Remove(pfi);
        }
        else
        {
            // 将偏移量和大小数据“移动”到 extraFiles
            AAPakFileInfo eFile = new AAPakFileInfo();
            eFile.name = "__unused__";
            eFile.offset = pfi.offset;
            eFile.size = pfi.size + pfi.paddingSize;
            eFile.sizeDuplicate = eFile.size;
            eFile.paddingSize = 0;
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
    /// 从 pak 中删除文件。根据 paddingDeleteMode 设置的不同，行为会有所不同
    /// </summary>
    /// <param name="filename">要从 pak 文件中删除的文件的文件名</param>
    /// <returns>成功或文件不存在则返回 true</returns>
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
            // 如果文件不存在则返回 true
            return true;
        }
    }

    /// <summary>
    /// 将新文件添加到 pak 中
    /// </summary>
    /// <param name="filename">pak 文件内文件的文件名</param>
    /// <param name="sourceStream">包含文件数据的源流</param>
    /// <param name="CreateTime">用作初始文件创建时间戳的时间</param>
    /// <param name="ModifyTime">用作最后修改时间戳的时间</param>
    /// <param name="autoSpareSpace">设置后，尝试在文件末尾预分配额外的可用空间，如果使用，则为文件大小的 25%。如果使用“已删除文件”，则忽略此参数</param>
    /// <param name="pfi">返回新创建文件的文件信息</param>
    /// <returns>成功则返回 true</returns>
    public bool AddAsNewFile(string filename, Stream sourceStream, DateTime CreateTime, DateTime ModifyTime, bool autoSpareSpace, out AAPakFileInfo pfi)
    {
        // 当我们有新文件，或者先前的空间不足时，我们会将其添加到文件表的起始位置，并移动文件表
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

        // 检查 extraFiles 中是否有可用的“未使用”空间
        for (int i = 0; i < extraFiles.Count; i++)
        {
            if (newFile.size <= extraFiles[i].size)
            {
                // 复制备用文件的属性并将其从 extraFiles 中删除
                newFile.offset = extraFiles[i].offset;
                newFile.paddingSize = (int)(extraFiles[i].size - newFile.size); // 这应该已经对齐了
                addedAtTheEnd = false;
                extraFiles.Remove(extraFiles[i]);
                break;
            }
        }

        if (addedAtTheEnd)
        {
            // 仅当在末尾添加时才需要计算填充
            var dif = (newFile.size % 0x200);
            if (dif > 0)
            {
                newFile.paddingSize = (int)(0x200 - dif);
            }
            if (autoSpareSpace)
            {
                // 如果使用 autoSpareSpace 添加文件，我们将保留一些额外的空间作为填充
                // 默认添加 25%
                var spareSpace = (newFile.size / 4);
                spareSpace -= (spareSpace % 0x200); // 对齐备用空间
                newFile.paddingSize += (int)spareSpace;
            }
        }

        // 添加到文件列表
        files.Add(newFile);

        isDirty = true;

        // 添加文件数据
        _gpFileStream.Position = newFile.offset;
        sourceStream.Position = 0;
        sourceStream.CopyTo(_gpFileStream);

        if (addedAtTheEnd)
        {
            _header.FirstFileInfoOffset = newFile.offset + newFile.size + newFile.paddingSize;
        }

        UpdateMD5(newFile); // TODO：优化此项，以便在复制流的同时进行计算

        // 设置输出
        pfi = newFile;
        return true;
    }

    /// <summary>
    /// 用来自 sourceStream 的数据添加或替换名为 filename 的给定文件
    /// </summary>
    /// <param name="filename">pak 内部使用的文件名</param>
    /// <param name="sourceStream">要添加的文件的源流</param>
    /// <param name="CreateTime">用作原始文件创建时间的时间</param>
    /// <param name="ModifyTime">用作最后修改时间的时间</param>
    /// <param name="autoSpareSpace">当不替换文件时，启用添加 sourceStream 大小 25% 的填充</param>
    /// <param name="pfi">新添加或修改的文件的 AAPakFileInfo</param>
    /// <returns>成功则返回 true</returns>
    public bool AddFileFromStream(string filename, Stream sourceStream, DateTime CreateTime, DateTime ModifyTime, bool autoSpareSpace, out AAPakFileInfo pfi)
    {
        pfi = nullAAPakFileInfo;
        if (readOnly)
        {
            return false;
        }

        bool addAsNew = true;
        // 尝试查找现有文件
        if (GetFileByName(filename, ref pfi))
        {
            var reservedSizeMax = pfi.size + pfi.paddingSize;
            addAsNew = (sourceStream.Length > reservedSizeMax);
            // 错误修复：如果空间不足，请确保也首先删除旧文件
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
    /// 将具有给定名称的文件添加到 pak 文件中
    /// </summary>
    /// <param name="sourceFileName">要添加的源文件的文件名</param>
    /// <param name="asFileName">在 pak 文件内使用的文件名</param>
    /// <param name="autoSpareSpace">设置后，尝试在文件末尾预分配额外的可用空间，如果使用，则为文件大小的 25%。如果使用“已删除文件”，则忽略此参数</param>
    /// <returns>成功则返回 true</returns>
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
    /// 将流转换为字符串
    /// </summary>
    /// <param name="stream">源流</param>
    /// <returns>流内部数据的字符串值</returns>
    static public string StreamToString(Stream stream)
    {
        stream.Position = 0;
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// 将字符串转换为 MemoryStream
    /// </summary>
    /// <param name="src">源字符串</param>
    /// <returns>包含源字符串数据的新 MemoryStream</returns>
    static public Stream StringToStream(string src)
    {
        byte[] byteArray = Encoding.UTF8.GetBytes(src);
        return new MemoryStream(byteArray);
    }
}

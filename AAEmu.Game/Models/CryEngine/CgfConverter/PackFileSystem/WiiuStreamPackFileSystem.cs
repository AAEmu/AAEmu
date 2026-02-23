using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CgfConverter.Services;
using CgfConverter.Utils;
using Extensions;

namespace CgfConverter.PackFileSystem;

public class WiiuStreamPackFileSystem : IPackFileSystem, IDisposable
{
    private readonly TaggedLogger Log = new("WiiuStreamPackFileSystem");
    public const string PackFileNameSuffix = ".wiiu.stream";

    private readonly Stream _stream;
    private readonly Mutex _streamMutex = new();
    private readonly Dictionary<string, FileEntry> _entries = new();

    public WiiuStreamPackFileSystem(Stream stream, Dictionary<string, string> options)
    {
        _stream = stream;

        var reader = new EndiannessChangeableBinaryReader(_stream, Encoding.UTF8, true);
        reader.IsBigEndian = true;

        if (reader.ReadUInt32() != 0x7374726d) // 'strm'
            throw new InvalidDataException();

        options = options.ToDictionary(x => x.Key, x => x.Value);

        var altCostume = false;
        if (options.Remove("alt", out var altCostumeString))
            altCostume = altCostumeString.IsTrueyString(true);

        foreach (var (k, v) in options)
            Log.W("Ignoring unknown parameter \"{0}\" with value \"{1}\".", k, v);

        while (reader.PeekChar() != -1)
        {
            var entry = new FileEntry(reader);
            if ((entry.Variant >= 0 && altCostume) || (entry.Variant <= 0 && !altCostume))
                _entries[entry.InnerPath.ToLowerInvariant()] = entry;

            reader.BaseStream.Seek(
                entry.CompressedSize == 0 ? entry.DecompressedSize : entry.CompressedSize,
                SeekOrigin.Current);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _stream.Dispose();
    }

    public Stream GetStream(string path) => new MemoryStream(ReadAllBytes(path));

    public bool Exists(string path) => _entries.ContainsKey(path.ToLowerInvariant());

    public string[] Glob(string pattern)
    {
        var regexPattern = new Regex(
            "^" +
            string.Join("[\\s\\S]*", Regex.Split(pattern, "\\*{2,}").Select(x =>
                string.Join("[^/\\\\]*", x.Split("*").Select(y =>
                    string.Join("[^/\\\\]", y.Split('?').Select(
                        Regex.Escape)))))) +
            "$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return _entries.Values.Where(x => regexPattern.IsMatch(x.InnerPath)).Select(x => x.InnerPath).ToArray();
    }

    public byte[] ReadAllBytes(string path)
    {
        if (!_entries.TryGetValue(path.ToLowerInvariant(), out FileEntry entry))
            throw new FileNotFoundException();

        var buffer = new byte[entry.DecompressedSize];

        Stream stream;
        var requireDispose = true;
        var requireMutex = false;

        switch (_stream)
        {
            case FileStream fs:
                stream = new FileStream(fs.Name, FileMode.Open, FileAccess.Read);
                break;
            case MemoryStream ms:
                stream = new MemoryStream(ms.GetBuffer(), 0, ms.Capacity, false);
                break;
            default:
                stream = _stream;
                requireMutex = true;
                requireDispose = false;
                _streamMutex.WaitOne();
                break;
        }

        try
        {
            stream.Seek(entry.Offset, SeekOrigin.Begin);
            if (entry.CompressedSize == 0)
            {
                if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
                    throw new EndOfStreamException();
            }
            else
            {
                using var ms = new MemoryStream(buffer, true);
                while (ms.Position < buffer.Length && stream.Position < entry.Offset + entry.CompressedSize)
                {
                    var size = stream.ReadCryIntWithFlag(out var backFlag);

                    if (backFlag)
                    {
                        var copyLength = stream.ReadCryInt();
                        var copyBaseOffset = (int) ms.Position - copyLength;

                        for (var remaining = size + 3; remaining > 0; remaining -= copyLength)
                            ms.Write(buffer, copyBaseOffset, Math.Min(copyLength, remaining));
                    }
                    else
                    {
                        if (stream.Read(buffer, (int) ms.Position, size) != size)
                            throw new EndOfStreamException();
                        ms.Position += size;
                    }
                }

                if (ms.Position != buffer.Length)
                    throw new EndOfStreamException();
            }
        } finally{
            if (requireDispose)
                stream.Dispose();
            if (requireMutex)
                _streamMutex.ReleaseMutex();
        }

        return buffer;
    }

    private readonly struct FileEntry : IComparable<FileEntry>
    {
        public readonly int CompressedSize;
        public readonly int DecompressedSize;
        public readonly int Hash;
        public readonly ushort UnknownValue1;
        public readonly short Variant;
        public readonly string InnerPath;
        public readonly long Offset;

        /// <summary>
        /// Create a dummy FileEntry for bisecting purposes.
        /// </summary>
        /// <param name="innerPath">Inner path.</param>
        public FileEntry(string innerPath)
        {
            InnerPath = FileHandlingExtensions.CombineAndNormalizePath(innerPath);
            Offset = CompressedSize = DecompressedSize = Hash = UnknownValue1 = 0;
            Variant = 0;
        }

        public FileEntry(BinaryReader reader)
        {
            reader.ReadInto(out CompressedSize);
            reader.ReadInto(out DecompressedSize);
            reader.ReadInto(out Hash);
            reader.ReadInto(out UnknownValue1);
            reader.ReadInto(out Variant);
            InnerPath = FileHandlingExtensions.CombineAndNormalizePath(reader.ReadCString());
            Offset = reader.BaseStream.Position;
        }

        public int CompareTo(FileEntry other) =>
            string.Compare(InnerPath, other.InnerPath, StringComparison.InvariantCultureIgnoreCase);
    }
}

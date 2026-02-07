using System.Buffers;
using System.Runtime.CompilerServices;

namespace AAEmu.Commons.Utils;

public static class SequenceReaderExtensions
{
    extension(ref SequenceReader<byte> reader)
    {
        public bool TryReadLittleEndian(out ushort value)
        {
            Unsafe.SkipInit(out value);
            return reader.TryReadLittleEndian(out Unsafe.As<ushort, short>(ref value));
        }

        public bool TryReadBigEndian(out ushort value)
        {
            Unsafe.SkipInit(out value);
            return reader.TryReadBigEndian(out Unsafe.As<ushort, short>(ref value));
        }

        public bool TryReadLittleEndian(out uint value)
        {
            Unsafe.SkipInit(out value);
            return reader.TryReadLittleEndian(out Unsafe.As<uint, int>(ref value));
        }

        public bool TryReadBigEndian(out uint value)
        {
            Unsafe.SkipInit(out value);
            return reader.TryReadBigEndian(out Unsafe.As<uint, int>(ref value));
        }

        public bool TryReadLittleEndian(out ulong value)
        {
            Unsafe.SkipInit(out value);
            return reader.TryReadLittleEndian(out Unsafe.As<ulong, long>(ref value));
        }

        public bool TryReadBigEndian(out ulong value)
        {
            Unsafe.SkipInit(out value);
            return reader.TryReadBigEndian(out Unsafe.As<ulong, long>(ref value));
        }
    }
}

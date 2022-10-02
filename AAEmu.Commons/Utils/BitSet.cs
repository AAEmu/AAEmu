using System.Collections;

namespace AAEmu.Commons.Utils
{
    public sealed class BitSet
    {
        private BitArray _bits;

        public int Count { get; private set; }

        public BitSet(int count)
        {
            Count = count;
            _bits = new BitArray(count);
        }

        public bool this[int index] => Get(index);

        public void Clear() => _bits.SetAll(false);
        public void Clear(int index) => _bits.Set(index, false);
        public void Set(int index) => _bits.Set(index, true);
        public bool Get(int index) => _bits.Get(index);

        public int NextSet(int startFrom)
        {
            var offset = startFrom;
            if (offset >= Count)
                return -1;
            var res = _bits.Get(offset);
            // locate non-empty slot
            while (!res)
            {
                if ((++offset) >= Count)
                    return -1;
                res = _bits.Get(offset);
            }

            return offset;
        }

        public int NextClear(int startFrom)
        {
            var offset = startFrom;
            if (offset >= Count)
                return -1;
            var res = _bits.Get(offset);
            // locate non-empty slot
            while (res)
            {
                if ((++offset) >= Count)
                    return -1;
                res = _bits.Get(offset);
            }

            return offset;
        }

        public void Or(BitSet other)
        {
            for (var i = 0; i < other.Count; i++)
                _bits[i] = other[i];
        }

        public int[] ToIntArray()
        {
            var result = new int[Count / 32];
            _bits.CopyTo(result, 0);
            return result;
        }

        public byte[] ToByteArray()
        {
            var result = new byte[Count / 8];
            _bits.CopyTo(result, 0);
            return result;
        }
    }
}

using System;

namespace AAEmu.Commons.IO.Bits
{
    /// <summary>
    /// Array of 32 bits. Fully unmanaged. Defaults to zeroes. Enumerable in C# 7.
    /// </summary>
    /// 
    /// <author>
    /// Jackson Dunstan, https://JacksonDunstan.com/articles/5172
    /// </author>
    /// 
    /// <license>
    /// MIT
    /// </license>
    public struct BitArray32 : IEquatable<BitArray32>
    {
#if CSHARP_7_OR_LATER
    /// <summary>
    /// Enumerates the bits of the array from least-significant to
    /// most-signficant. It's OK to change the array while enumerating.
    /// </summary>
    public unsafe ref struct Enumerator
    {
        /// <summary>
        /// Pointer to the bits
        /// </summary>
        private readonly uint* m_Bits;
 
        /// <summary>
        /// Index into the bits
        /// </summary>
        private int m_Index;
 
        /// <summary>
        /// Create the enumerator with index at -1
        /// </summary>
        /// 
        /// <param name="bits">
        /// Bits to enumerate
        /// </param>
        public Enumerator(uint* bits)
        {
            m_Bits = bits;
            m_Index = -1;
        }
 
        /// <summary>
        /// Move to the next bit
        /// </summary>
        /// 
        /// <returns>
        /// If a bit is available via <see cref="Current"/>. If not, enumeration
        /// is done.
        /// </returns>
        public bool MoveNext()
        {
            m_Index++;
            return m_Index < 32;
        }
 
        /// <summary>
        /// Get the current bit. If <see cref="MoveNext"/> has not been called
        /// or the last call of <see cref="MoveNext"/> returned false, this
        /// function asserts.
        /// </summary>
        /// 
        /// <value>
        /// The current bit
        /// </value>
        public bool Current
        {
            get
            {
                RequireIndexInBounds();
                uint mask = 1u << m_Index;
                return (*m_Bits & mask) == mask;
            }
        }
 
        /// <summary>
        /// Assert if <see cref="m_Index"/> isn't in bounds
        /// </summary>
        [BurstDiscard]
        public void RequireIndexInBounds()
        {
            Assert.IsTrue(
                m_Index >= 0 && m_Index < 32,
                "Index out of bounds: " + m_Index);
        }
    }
#endif

        /// <summary>
        /// Integer whose bits make up the array
        /// </summary>
        public uint Bits;

        /// <summary>
        /// Create the array with the given bits
        /// </summary>
        /// 
        /// <param name="bits">
        /// Bits to make up the array
        /// </param>
        public BitArray32(uint bits)
        {
            Bits = bits;
        }

        /// <summary>
        /// Get or set the bit at the given index. For faster getting of multiple
        /// bits, use <see cref="GetBits(uint)"/>. For faster setting of single
        /// bits, use <see cref="SetBit(int)"/> or <see cref="UnsetBit(int)"/>. For
        /// faster setting of multiple bits, use <see cref="SetBits(uint)"/> or
        /// <see cref="UnsetBits(uint)"/>.
        /// </summary>
        /// 
        /// <param name="index">
        /// Index of the bit to get or set
        /// </param>
        public bool this[int index]
        {
            get
            {
                RequireIndexInBounds(index);
                uint mask = 1u << index;
                return (Bits & mask) == mask;
            }
            set
            {
                RequireIndexInBounds(index);
                uint mask = 1u << index;
                if (value)
                {
                    Bits |= mask;
                }
                else
                {
                    Bits &= ~mask;
                }
            }
        }

        /// <summary>
        /// Get the length of the array
        /// </summary>
        /// 
        /// <value>
        /// The length of the array. Always 32.
        /// </value>
        public int Length
        {
            get
            {
                return 32;
            }
        }

        /// <summary>
        /// Set a single bit to 1
        /// </summary>
        /// 
        /// <param name="index">
        /// Index of the bit to set. Asserts if not on [0:31].
        /// </param>
        public void SetBit(int index)
        {
            RequireIndexInBounds(index);
            uint mask = 1u << index;
            Bits |= mask;
        }

        /// <summary>
        /// Set a single bit to 0
        /// </summary>
        /// 
        /// <param name="index">
        /// Index of the bit to unset. Asserts if not on [0:31].
        /// </param>
        public void UnsetBit(int index)
        {
            RequireIndexInBounds(index);
            uint mask = 1u << index;
            Bits &= ~mask;
        }

        /// <summary>
        /// Get all the bits that match a mask
        /// </summary>
        /// 
        /// <param name="mask">
        /// Mask of bits to get
        /// </param>
        /// 
        /// <returns>
        /// The bits that match the given mask
        /// </returns>
        public uint GetBits(uint mask)
        {
            return Bits & mask;
        }

        /// <summary>
        /// Set all the bits that match a mask to 1
        /// </summary>
        /// 
        /// <param name="mask">
        /// Mask of bits to set
        /// </param>
        public void SetBits(uint mask)
        {
            Bits |= mask;
        }

        /// <summary>
        /// Set all the bits that match a mask to 0
        /// </summary>
        /// 
        /// <param name="mask">
        /// Mask of bits to unset
        /// </param>
        public void UnsetBits(uint mask)
        {
            Bits &= ~mask;
        }

        /// <summary>
        /// Check if this array equals an object
        /// </summary>
        /// 
        /// <param name="obj">
        /// Object to check. May be null.
        /// </param>
        /// 
        /// <returns>
        /// If the given object is a BitArray32 and its bits are the same as this
        /// array's bits
        /// </returns>
        public override bool Equals(object obj)
        {
            return obj is BitArray32 && Bits == ((BitArray32)obj).Bits;
        }

        /// <summary>
        /// Check if this array equals another array
        /// </summary>
        /// 
        /// <param name="arr">
        /// Array to check
        /// </param>
        /// 
        /// <returns>
        /// If the given array's bits are the same as this array's bits
        /// </returns>
        public bool Equals(BitArray32 arr)
        {
            return Bits == arr.Bits;
        }

        /// <summary>
        /// Get the hash code of this array
        /// </summary>
        /// 
        /// <returns>
        /// The hash code of this array, which is the same as
        /// the hash code of <see cref="Bits"/>.
        /// </returns>
        public override int GetHashCode()
        {
            return Bits.GetHashCode();
        }

        /// <summary>
        /// Get a string representation of the array
        /// </summary>
        /// 
        /// <returns>
        /// A newly-allocated string representing the bits of the array.
        /// </returns>
        public override string ToString()
        {
            const string header = "BitArray32{";
            const int headerLen = 11; // must be header.Length
            char[] chars = new char[headerLen + 32 + 1];
            int i = 0;
            for (; i < headerLen; ++i)
            {
                chars[i] = header[i];
            }

            for (uint num = 1u << 31; num > 0; num >>= 1, ++i)
            {
                chars[i] = (Bits & num) != 0 ? '1' : '0';
            }

            chars[i] = '}';
            return new string(chars);
        }

        /// <summary>
        /// Assert if the given index isn't in bounds
        /// </summary>
        /// 
        /// <param name="index">
        /// Index to check
        /// </param>
        public void RequireIndexInBounds(int index)
        {
            if (index < 0 || index > 32)
                throw new ArgumentOutOfRangeException($"Index out of bounds: {index}");
        }

#if CSHARP_7_OR_LATER
    /// <summary>
    /// Get an enumerator for this array's bits
    /// </summary>
    /// 
    /// <returns>
    /// An enumerator for this array's bits
    /// </returns>
    public unsafe Enumerator GetEnumerator()
    {
        // Safe because Enumerator is a 'ref struct'
        fixed (uint* bits = &Bits)
        {
            return new Enumerator(bits);
        }
    }
#endif
    }
}

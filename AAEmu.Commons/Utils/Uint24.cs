/*
 * author: csh aka Connor Spencer Harries
 */
using System;

namespace AAEmu.Commons.Utils
{
    public struct Uint24
    {
        /// <summary>
        /// Представляет наибольшее возможное значение типа
        /// Это поле является константой.
        /// </summary>
        public const uint MaxValue = 0x00FFFFFF;
        
        /// <summary>
        /// Представляет минимально допустимое значение типа
        /// Это поле является константой.
        /// </summary>
        public const uint MinValue = 0x00000000;

        private uint _val;

        public void Update(byte[] val)
        {
            if (val.Length < 3)
            {
                throw new ArgumentException("array must consist of three or more bytes", nameof(val));
            }
            _val = (uint)(val[0] | val[1] << 8 | val[2] << 16);
        }

        public bool Equals(Uint24 other)
        {
            return _val == other._val;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            return obj is Uint24 && Equals((Uint24)obj);
        }

        public override int GetHashCode()
        {
            return (int)_val;
        }

        public override string ToString()
        {
            return ToString(true);
        }

        public string ToString(bool asNumber)
        {
            if (asNumber)
            {
                return $"{_val}";
            }
            char[] bits = new char[24];
            uint val = _val;
            for (int i = 0; val != 0; i++)
            {
                bits[i] = (val & 1) == 1 ? '1' : '0';
                val >>= 1;
            }
            Array.Reverse(bits);
            return new string(bits);
        }

        public byte this[int idx]
        {
            get
            {
                switch (idx)
                {
                    case 0:
                        return (byte)_val;
                    case 1:
                        return (byte)(_val << 8);
                    case 2:
                        return (byte)(_val << 16);
                    default:
                        throw new IndexOutOfRangeException($"Expected idx > 0 && idx < 3 to be true. Received {idx}.");
                }
            }
            set
            {
                // optimisation
                if (_val == 0 && idx == 0)
                {
                    _val = value;
                    return;
                }
                byte[] newVal = BitConverter.GetBytes(_val);
                newVal[idx] = value;
                Update(newVal);
            }
        }

        public static implicit operator uint(Uint24 val)
        {
            return val._val;
        }

        public static implicit operator Uint24(int val)
        {
            return (uint)val;
        }

        public static implicit operator Uint24(uint val)
        {
            return new Uint24
            {
                _val = val & MaxValue
            };
        }

        public static Uint24 operator +(Uint24 left, Uint24 right)
        {
            return left._val + right._val;
        }

        public static Uint24 operator -(Uint24 left, Uint24 right)
        {
            return left._val - right._val;
        }

        public static bool operator >(Uint24 left, Uint24 right)
        {
            return left._val > right._val;
        }

        public static bool operator <(Uint24 left, Uint24 right)
        {
            return left._val < right._val;
        }

        public static bool operator ==(Uint24 left, Uint24 right)
        {
            return left._val == right._val;
        }

        public static bool operator !=(Uint24 left, Uint24 right)
        {
            return !(left == right);
        }

        public static bool operator <=(Uint24 left, Uint24 right)
        {
            return left == right || left < right;
        }

        public static bool operator >=(Uint24 left, Uint24 right)
        {
            return left == right || left > right;
        }

        public static Uint24 operator ++(Uint24 val)
        {
            val._val = val._val + 1;
            return val;
        }

        public static Uint24 operator --(Uint24 val)
        {
            val._val = val._val - 1;
            return val;
        }
    }
}

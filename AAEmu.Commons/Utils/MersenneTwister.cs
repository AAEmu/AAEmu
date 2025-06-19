// Copyright 2007-2008 Rory Plaire (codekaizen@gmail.com)

// Adapted from:

/* C# Version Copyright (C) 2001-2004 Akihilo Kramot (Takel).  */
/* C# porting from a C-program for MT19937, originaly coded by */
/* Takuji Nishimura and Makoto Matsumoto, considering the suggestions by */
/* Topher Cooper and Marc Rieffel in July-Aug. 1997.           */
/* This library is free software under the Artistic license:   */
/*                                                             */
/* You can find the original C-program at                      */
/*     http://www.math.keio.ac.jp/~matumoto/mt.html            */
/*                                                             */

// and:

/////////////////////////////////////////////////////////////////////////////
// C# Version Copyright (c) 2003 CenterSpace Software, LLC                 //
//                                                                         //
// This code is free software under the Artistic license.                  //
//                                                                         //
// CenterSpace Software                                                    //
// 2098 NW Myrtlewood Way                                                  //
// Corvallis, Oregon, 97330                                                //
// USA                                                                     //
// http://www.centerspace.net                                              //
/////////////////////////////////////////////////////////////////////////////

// and, of course:
/* 
   A C-program for MT19937, with initialization improved 2002/2/10.
   Coded by Takuji Nishimura and Makoto Matsumoto.
   This is a faster version by taking Shawn Cokus's optimization,
   Matthe Bellew's simplification, Isaku Wada's real version.

   Before using, initialize the state by using init_genrand(seed) 
   or init_by_array(init_key, key_length).

   Copyright (C) 1997 - 2002, Makoto Matsumoto and Takuji Nishimura,
   All rights reserved.                          

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

     1. Redistributions of source code must retain the above copyright
        notice, this list of conditions and the following disclaimer.

     2. Redistributions in binary form must reproduce the above copyright
        notice, this list of conditions and the following disclaimer in the
        documentation and/or other materials provided with the distribution.

     3. The names of its contributors may not be used to endorse or promote 
        products derived from this software without specific prior written 
        permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR
   CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


   Any feedback is very welcome.
   http://www.math.sci.hiroshima-u.ac.jp/~m-mat/MT/emt.html
   email: m-mat @ math.sci.hiroshima-u.ac.jp (remove space)
*/

using System;

namespace AAEmu.Commons.Utils;

/// <summary>
/// 使用马特赛特旋转算法生成伪随机数。
/// </summary>
/// <remarks>
/// 有关该算法的详细信息，请参见 <a href="http://www.math.sci.hiroshima-u.ac.jp/~m-mat/MT/emt.html">
/// http://www.math.sci.hiroshima-u.ac.jp/~m-mat/MT/emt.html</a>。
/// </remarks>
public class MersenneTwister : Random
{
    /// <summary>
    /// 使用给定的种子创建一个新的伪随机数生成器。
    /// </summary>
    /// <param name="seed">用作种子的值。</param>
    public MersenneTwister(int seed)
    {
        init((uint)seed);
    }

    /// <summary>
    /// 使用默认种子创建一个新的伪随机数生成器。
    /// </summary>
    /// <remarks>
    /// 使用 <c>new <see cref="Random"/>().<see cref="Random.Next()"/></c>
    /// 作为种子。
    /// </remarks>
    public MersenneTwister()
        : this(new Random().Next()) /* 使用默认的初始种子 */
    {
    }

    /// <summary>
    /// 创建一个使用给定数组初始化的伪随机数生成器。
    /// </summary>
    /// <param name="initKey">用于初始化密钥的数组。</param>
    public MersenneTwister(int[] initKey)
    {
        if (initKey == null)
            throw new ArgumentNullException(nameof(initKey));

        var initArray = new uint[initKey.Length];

        for (var i = 0; i < initKey.Length; ++i)
        {
            initArray[i] = (uint)initKey[i];
        }

        init(initArray);
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="uint"/>。
    /// </summary>
    /// <returns>一个伪随机 <see cref="uint"/> 值。</returns>
    public virtual uint NextUInt32()
    {
        return GenerateUInt32();
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="uint"/>，
    /// 最大不超过 <paramref name="maxValue"/>。
    /// </summary>
    /// <param name="maxValue">
    /// 要创建的伪随机数的最大值。
    /// </param>
    /// <returns>
    /// 一个伪随机 <see cref="uint"/> 值，其最大为 <paramref name="maxValue"/>。
    /// </returns>
    public virtual uint NextUInt32(uint maxValue)
    {
        return (uint)(GenerateUInt32() / ((double)uint.MaxValue / maxValue));
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="uint"/>，其值至少为
    /// <paramref name="minValue"/> 且最大不超过 <paramref name="maxValue"/>。
    /// </summary>
    /// <param name="minValue">要创建的伪随机数的最小值。</param>
    /// <param name="maxValue">要创建的伪随机数的最大值。</param>
    /// <returns>
    /// 一个伪随机 <see cref="uint"/> 值，其值至少为
    /// <paramref name="minValue"/> 且最大为 <paramref name="maxValue"/>。
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 如果 <c><paramref name="minValue"/> &gt;= <paramref name="maxValue"/></c>。
    /// </exception>
    public virtual uint NextUInt32(uint minValue, uint maxValue) /* 抛出 ArgumentOutOfRangeException */
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), $"{nameof(minValue)} is greater than {nameof(maxValue)}");

        return (uint)(GenerateUInt32() / ((double)uint.MaxValue / (maxValue - minValue)) + minValue);
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="int"/>。
    /// </summary>
    /// <returns>一个伪随机 <see cref="int"/> 值。</returns>
    public override int Next()
    {
        return Next(int.MaxValue);
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="int"/>，最大不超过 <paramref name="maxValue"/>。
    /// </summary>
    /// <param name="maxValue">要创建的伪随机数的最大值。</param>
    /// <returns>
    /// 一个伪随机 <see cref="int"/> 值，其最大为 <paramref name="maxValue"/>。
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 当 <paramref name="maxValue"/> &lt; 0 时。
    /// </exception>
    public override int Next(int maxValue)
    {
        if (maxValue < 1)
        {
            if (maxValue < 0)
                throw new ArgumentOutOfRangeException(nameof(maxValue));
            return 0;
        }

        return (int)(NextDouble() * maxValue);
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="int"/>，
    /// 其值至少为 <paramref name="minValue"/>
    /// 且最大不超过 <paramref name="maxValue"/>。
    /// </summary>
    /// <param name="minValue">要创建的伪随机数的最小值。</param>
    /// <param name="maxValue">要创建的伪随机数的最大值。</param>
    /// <returns>一个伪随机 Int32 值，其值至少为 <paramref name="minValue"/> 且
    /// 最大为 <paramref name="maxValue"/>。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 如果 <c><paramref name="minValue"/> &gt;= <paramref name="maxValue"/></c>。
    /// </exception>
    public override int Next(int minValue, int maxValue)
    {
        if (maxValue < minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), $"{nameof(maxValue)} is lesser than {nameof(minValue)}");

        if (maxValue == minValue)
            return minValue;

        return Next(maxValue - minValue) + minValue;
    }

    /// <summary>
    /// 用伪随机字节填充缓冲区。
    /// </summary>
    /// <param name="buffer">要填充的缓冲区。</param>
    /// <exception cref="ArgumentNullException">
    /// 如果 <c><paramref name="buffer"/> == <see langword="null"/></c>。
    /// </exception>
    public override void NextBytes(byte[] buffer)
    {
        // [codekaizen: 已更正此问题，以便在检查长度之前检查 null。]
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        var bufLen = buffer.Length;
        for (var idx = 0; idx < bufLen; ++idx)
            buffer[idx] = (byte)Next(256);
    }

    /// <summary>
    /// 返回下一个伪随机 <see cref="double"/> 值。
    /// </summary>
    /// <returns>一个伪随机双精度浮点值。</returns>
    /// <remarks>
    /// <para>
    /// 使用 MT19937 创建双精度浮点数有两种常见方法：
    /// 使用 <see cref="GenerateUInt32"/> 并除以 0xFFFFFFFF + 1，
    /// 或者生成两个双字，将第一个移位 26 位并加上第二个。
    /// </para>
    /// <para>
    /// 在《Monte Carlo Methods and Applications》杂志第 12 卷第 5-6 期第 385 – 393 页（2006 年）
    /// 发表的一篇题为“伪随机数生成器的重复性测试”的关于 MT19937 随机性测量的最新研究中，
    /// 发现在测量算法生成的数字序列中特定数字的预期重复次数时，
    /// 生成双精度浮点数的 32 位版本在 95% 置信水平下失败。
    /// </para>
    /// <para>
    /// 因此，此处实现了 53 位方法，而未实现生成双精度浮点数的 32 位方法。
    /// 如果由于某种原因需要 32 位方法，可以通过以下方式生成：
    /// <code>
    /// (Double)NextUInt32() / ((UInt64)UInt32.MaxValue + 1);
    /// </code>
    /// </para>
    /// </remarks>
    public override double NextDouble()
    {
        return Compute53BitRandom(0, InverseOnePlus53BitsOf1S);
    }

    /// <summary>
    /// 返回一个大于或等于零的伪随机数，并且根据给定参数的值，
    /// 该数严格小于一或小于或等于一。
    /// </summary>
    /// <param name="includeOne">
    /// 如果为 <see langword="true"/>，则返回的伪随机数将小于或等于一；
    /// 否则，返回的伪随机数将严格小于一。
    /// </param>
    /// <returns>
    /// 如果 <paramref name="includeOne"/> 为 <see langword="true"/>，
    /// 此方法返回一个大于或等于零且小于或等于一的双精度伪随机数。
    /// 如果 <paramref name="includeOne"/> 为 <see langword="false"/>，此方法
    /// 返回一个大于或等于零且严格小于一的双精度伪随机数。
    /// </returns>
    public double NextDouble(bool includeOne)
    {
        return includeOne ? Compute53BitRandom(0, Inverse53BitsOf1S) : NextDouble();
    }

    /// <summary>
    /// 返回一个大于 0.0 且小于 1.0 的伪随机数。
    /// </summary>
    /// <returns>一个大于 0.0 且小于 1.0 的伪随机数。</returns>
    public double NextDoublePositive()
    {
        return Compute53BitRandom(0.5, Inverse53BitsOf1S);
    }

    /// <summary>
    /// 返回一个介于 0.0 和 1.0 之间的伪随机数。
    /// </summary>
    /// <returns>
    /// 一个大于或等于 0.0 且小于 1.0 的单精度浮点数。
    /// </returns>
    public new float NextSingle()
    {
        return (float)NextDouble();
    }

    /// <summary>
    /// 返回一个大于或等于零的伪随机数，并且根据给定布尔参数的值，
    /// 该数严格小于一或小于或等于一。
    /// </summary>
    /// <param name="includeOne">
    /// 如果为 <see langword="true"/>，则返回的伪随机数将小于或等于一；
    /// 否则，返回的伪随机数将严格小于一。
    /// </param>
    /// <returns>
    /// 如果 <paramref name="includeOne"/> 为 <see langword="true"/>，此方法返回一个
    /// 大于或等于零且小于或等于一的单精度伪随机数。
    /// 如果 <paramref name="includeOne"/> 为 <see langword="false"/>，此方法
    /// 返回一个大于或等于零且严格小于一的单精度伪随机数。
    /// </returns>
    public float NextSingle(bool includeOne)
    {
        return (float)NextDouble(includeOne);
    }

    /// <summary>
    /// 返回一个大于 0.0 且小于 1.0 的伪随机数。
    /// </summary>
    /// <returns>一个大于 0.0 且小于 1.0 的伪随机数。</returns>
    public float NextSinglePositive()
    {
        return (float)NextDoublePositive();
    }

    /// <summary>
    /// 生成一个新的伪随机 <see cref="uint"/>。
    /// </summary>
    /// <returns>一个伪随机 <see cref="uint"/>。</returns>
    protected uint GenerateUInt32()
    {
        uint y;

        /* _mag01[x] = x * MatrixA  对于 x=0,1 */
        if (_mti >= N) /* 一次生成 N 个字 */
        {
            short kk = 0;

            for (; kk < N - M; ++kk)
            {
                y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ _mag01[y & 0x1];
            }

            for (; kk < N - 1; ++kk)
            {
                y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ _mag01[y & 0x1];
            }

            y = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
            _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ _mag01[y & 0x1];

            _mti = 0;
        }

        y = _mt[_mti++];
        y ^= temperingShiftU(y);
        y ^= temperingShiftS(y) & TemperingMaskB;
        y ^= temperingShiftT(y) & TemperingMaskC;
        y ^= temperingShiftL(y);

        return y;
    }

    /* 周期参数 */
    private const int N = 624;
    private const int M = 397;
    private const uint MatrixA = 0x9908b0df; /* 常量向量 a */
    private const uint UpperMask = 0x80000000; /* 最高有效 w-r 位 */
    private const uint LowerMask = 0x7fffffff; /* 最低有效 r 位 */

    /* 回火参数 */
    private const uint TemperingMaskB = 0x9d2c5680;
    private const uint TemperingMaskC = 0xefc60000;

    private static uint temperingShiftU(uint y)
    {
        return (y >> 11);
    }

    private static uint temperingShiftS(uint y)
    {
        return (y << 7);
    }

    private static uint temperingShiftT(uint y)
    {
        return (y << 15);
    }

    private static uint temperingShiftL(uint y)
    {
        return (y >> 18);
    }

    private readonly uint[] _mt = new uint[N]; /* 状态向量数组 */
    private short _mti;

    private static readonly uint[] _mag01 = { 0x0, MatrixA };

    private void init(uint seed)
    {
        _mt[0] = seed & 0xffffffffU;

        for (_mti = 1; _mti < N; _mti++)
        {
            _mt[_mti] = (uint)(1812433253U * (_mt[_mti - 1] ^ (_mt[_mti - 1] >> 30)) + _mti);
            // 乘数请参见 Knuth TAOCP 第 2 卷第 3 版第 106 页。
            // 在先前版本中，种子的最高有效位会影响
            // 仅数组 _mt[] 的最高有效位。
            // 2002/01/09 由 Makoto Matsumoto 修改
            _mt[_mti] &= 0xffffffffU;
            // 适用于超过 32 位的机器
        }
    }

    private void init(uint[] key)
    {
        init(19650218U);

        var keyLength = key.Length;
        var i = 1;
        var j = 0;
        var k = (N > keyLength ? N : keyLength);

        for (; k > 0; k--)
        {
            _mt[i] = (uint)((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525U)) + key[j] +
                             j); /* 非线性 */
            _mt[i] &= 0xffffffffU; // 适用于超过 32 位的机器
            i++;
            j++;
            if (i >= N)
            {
                _mt[0] = _mt[N - 1];
                i = 1;
            }

            if (j >= keyLength) j = 0;
        }

        for (k = N - 1; k > 0; k--)
        {
            _mt[i] = (uint)((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941U)) - i); /* 非线性 */
            _mt[i] &= 0xffffffffU; // 适用于超过 32 位的机器
            i++;

            if (i < N)
            {
                continue;
            }

            _mt[0] = _mt[N - 1];
            i = 1;
        }

        _mt[0] = 0x80000000U; // 最高有效位为 1；确保初始数组非零
    }


    // 9007199254740991.0 是当指数为 0 时，53 位有效数字可以表示的最大双精度浮点数值。
    private const double FiftyThreeBitsOf1S = 9007199254740991.0;

    // 乘以逆数以（徒劳地？）尝试避免除法。
    private const double Inverse53BitsOf1S = 1.0 / FiftyThreeBitsOf1S;
    private const double OnePlus53BitsOf1S = FiftyThreeBitsOf1S + 1;
    private const double InverseOnePlus53BitsOf1S = 1.0 / OnePlus53BitsOf1S;

    private double Compute53BitRandom(double translate, double scale)
    {
        // 获取 27 个伪随机位
        var a = (ulong)GenerateUInt32() >> 5;
        // 获取 26 个伪随机位
        var b = (ulong)GenerateUInt32() >> 6;

        // 将 27 个伪随机位 (a) 左移 26 位 (* 67108864.0) 并
        // 加上另外 26 个伪随机位 (+ b)。
        return ((a * 67108864.0 + b) + translate) * scale;

        // 用下面的代码代替上面的怎么样？乘法更好吗？
        // 为什么？（是 FMUL 指令吗？这在 .Net 中有效吗？JIT 编译器会注意到吗？）
        //return BitConverter.Int64BitsToDouble((a << 26) + b)); // 将长整型位转换为双精度浮点数
    }
}

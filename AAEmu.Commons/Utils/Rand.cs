using System;

namespace AAEmu.Commons.Utils;

/// <summary>
/// 提供一个线程安全的静态类，用于生成伪随机数。
/// 此类使用 <see cref="MersenneTwister"/> 作为其底层的随机数生成器，
///并通过锁定机制确保多线程环境下的安全访问。
/// </summary>
public static class Rand
{
    private static MersenneTwister _random = new(DateTime.UtcNow.Millisecond); // 底层的马特赛特旋转伪随机数生成器实例。
    private static object _lock = new(); // 用于确保对 _random 实例的线程安全访问的锁对象。

    /// <summary>
    /// 返回一个非负的随机整数（线程安全）。
    /// </summary>
    /// <returns>大于或等于零且小于 <see cref="int.MaxValue"/> 的32位有符号整数。</returns>
    public static int Next()
    {
        lock (_lock)
        {
            return _random.Next();
        }
    }

    /// <summary>
    /// 返回一个小于指定最大值的非负随机整数（线程安全）。
    /// </summary>
    /// <param name="maxValue">要生成的随机数的上限（随机数将小于此值）。此值必须大于或等于零。</param>
    /// <returns>大于或等于零且小于 <paramref name="maxValue"/> 的32位有符号整数。</returns>
    public static int Next(int maxValue)
    {
        lock (_lock)
        {
            return _random.Next(maxValue);
        }
    }

    /// <summary>
    /// 返回指定范围内的随机整数（线程安全）。
    /// </summary>
    /// <param name="minValue">返回的随机数的下限（含）。</param>
    /// <param name="maxValue">返回的随机数的上限（不含）。<paramref name="maxValue"/> 必须大于或等于 <paramref name="minValue"/>。</param>
    /// <returns>一个大于或等于 <paramref name="minValue"/> 且小于 <paramref name="maxValue"/> 的32位有符号整数。</returns>
    public static int Next(int minValue, int maxValue)
    {
        lock (_lock)
        {
            return _random.Next(minValue, maxValue);
        }
    }

    /// <summary>
    /// 返回一个大于或等于 0.0 且小于或等于 1.0 的随机浮点数（线程安全）。
    /// </summary>
    /// <returns>大于或等于 0.0 且小于或等于 1.0 的双精度浮点数。</returns>
    public static double NextDouble()
    {
        lock (_lock)
        {
            return _random.NextDouble(true); // includeOne = true 确保结果可以为 1.0
        }
    }

    /// <summary>
    /// 返回一个大于或等于 0.0f 且小于或等于 1.0f 的随机浮点数（线程安全）。
    /// </summary>
    /// <returns>大于或等于 0.0f 且小于或等于 1.0f 的单精度浮点数。</returns>
    public static float NextSingle()
    {
        lock (_lock)
        {
            return _random.NextSingle(true); // includeOne = true 确保结果可以为 1.0f
        }
    }

    /// <summary>
    /// 返回一个小于指定最大值的非负随机浮点数（线程安全）。
    /// </summary>
    /// <param name="maxValue">要生成的随机数的上限（随机数将小于此值）。</param>
    /// <returns>大于或等于 0.0f 且小于 <paramref name="maxValue"/> 的单精度浮点数。</returns>
    public static float Next(float maxValue)
    {
        lock (_lock)
        {
            return _random.NextSingle(true) * maxValue;
        }
    }

    /// <summary>
    /// 返回指定范围内的随机浮点数（线程安全）。
    /// </summary>
    /// <param name="minValue">返回的随机数的下限（含）。</param>
    /// <param name="maxValue">返回的随机数的上限（通常不含，取决于实现细节，此处表现为可能包含）。</param>
    /// <returns>一个大于或等于 <paramref name="minValue"/> 且小于 <paramref name="maxValue"/> 的单精度浮点数。</returns>
    public static float Next(float minValue, float maxValue)
    {
        lock (_lock)
        {
            return _random.NextSingle(true) * (maxValue - minValue) + minValue;
        }
    }
}

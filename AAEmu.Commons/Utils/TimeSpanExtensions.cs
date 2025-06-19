using System;

namespace AAEmu.Commons.Utils;

/// <summary>
/// 提供对 <see cref="TimeSpan"/> 类型的扩展方法。
/// </summary>
public static class TimeSpanExtensions
{
    /// <summary>
    /// 检查给定的 <see cref="TimeSpan"/> 是否位于指定的开始时间和结束时间之间。
    /// 此方法能够正确处理跨越午夜的时间范围（例如，晚上10点到凌晨2点）。
    /// </summary>
    /// <param name="time">要检查的 <see cref="TimeSpan"/>。</param>
    /// <param name="startTime">时间范围的开始时间。</param>
    /// <param name="endTime">时间范围的结束时间。</param>
    /// <returns>如果 <paramref name="time"/> 位于 <paramref name="startTime"/> 和 <paramref name="endTime"/> 之间（包含边界），则为 true；否则为 false。如果开始时间和结束时间相同，则始终返回 true。</returns>
    public static bool IsBetween(this TimeSpan time, TimeSpan startTime, TimeSpan endTime)
    {
        if (endTime == startTime) // 如果开始和结束时间相同，则认为任何时间都在此“点”范围内。
        {
            return true;
        }

        if (endTime < startTime) // 处理跨越午夜的时间范围，例如 startTime = 22:00, endTime = 02:00
        {
            // 对于跨午夜的情况，时间要么小于等于结束时间（在午夜后），要么大于等于开始时间（在午夜前）。
            return time <= endTime || time >= startTime;
        }

        // 对于未跨越午夜的正常时间范围。
        return time >= startTime && time <= endTime;
    }
}

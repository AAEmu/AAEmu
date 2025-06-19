using System;
using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Commons.Utils;

/// <summary>
/// 提供 <see cref="IEnumerable{T}"/> 的扩展方法。
/// </summary>
public static class IEnumerableExtensions
{
    /// <summary>
    /// 根据每个元素关联的权重，从序列中随机选择一个元素。
    /// 权重较高的元素被选中的概率较大。
    /// </summary>
    /// <typeparam name="T">序列中元素的类型。</typeparam>
    /// <param name="sequence">要从中选择元素的序列。</param>
    /// <param name="weightSelector">一个函数，用于从元素中提取权重值（浮点数）。</param>
    /// <returns>根据权重随机选择的元素；如果序列为空，则返回 <typeparamref name="T"/> 的默认值。</returns>
    public static T RandomElementByWeight<T>(this IEnumerable<T> sequence, Func<T, float> weightSelector)
    {
        float totalWeight = sequence.Sum(weightSelector); // 计算所有元素权重的总和。
        // 我们要查找的权重...
        float itemWeightIndex = Rand.NextSingle() * totalWeight;
        float currentWeightIndex = 0.0f;

        foreach (var item in from weightedItem in sequence
                             select new { Value = weightedItem, Weight = weightSelector(weightedItem) })
        {
            currentWeightIndex += item.Weight;

            // 如果此项的权重达到或超过了我们要查找的权重，那么它就是我们想要的项....
            if (currentWeightIndex >= itemWeightIndex)
                return item.Value;
        }

        return default;
    }
}

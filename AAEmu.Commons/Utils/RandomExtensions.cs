using System;

namespace AAEmu.Commons.Utils;

public static class RandomExtensions
{
    /// <summary>
    /// Returns a random floating-point number that is within a specified range.
    /// </summary>
    /// <param name="random">The <see cref="Random"/> object to use to generate the number.</param>
    /// <param name="minValue">The inclusive lower bound of the random number returned.</param>
    /// <param name="maxValue">
    /// The exclusive upper bound of the random number returned.
    /// <paramref name="maxValue"/> must be greater than or equal to <paramref name="minValue"/>.
    /// </param>
    /// <returns>
    /// A single-precision floating point number that is greater than or equal to <paramref name="minValue"/> and less
    /// than <paramref name="maxValue"/>; that is, the range of return values includes <paramref name="minValue"/> but
    /// not <paramref name="maxValue"/>. If <paramref name="minValue"/> equals <paramref name="maxValue"/>,
    /// <paramref name="minValue"/> is returned.
    /// </returns>
    public static float Next(this Random random, float minValue, float maxValue) =>
        random.NextSingle() * (maxValue - minValue) + minValue;
}

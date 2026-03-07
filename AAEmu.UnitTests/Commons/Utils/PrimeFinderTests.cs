using Xunit;

using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class PrimeFinderTests
{
    [Fact]
    public void NextPrime_ShouldReturnNextPrime_WhenCapacityIsNotPrime()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = 100;

        // Act
        var result = PrimeFinder.NextPrime(desiredCapacity);

        // Assert
        Assert.InRange(result, 101, int.MaxValue);
    }

    [Fact]
    public void NextPrime_ShouldReturnLargerPrime_WhenCapacityIsPrime()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = 97;

        // Act
        var result = PrimeFinder.NextPrime(desiredCapacity);

        // Assert
        Assert.InRange(result, 98, int.MaxValue);
        Assert.NotEqual(97, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void NextPrime_ShouldReturnValidPrime_WhenGivenVariousCapacities(int capacity)
    {
        // Arrange
        PrimeFinder.Init();

        // Act
        var result = PrimeFinder.NextPrime(capacity);

        // Assert
        Assert.InRange(result, capacity, int.MaxValue);
    }

    [Fact]
    public void NextPrime_ShouldReturnAtLeastGivenCapacity()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = 1000;

        // Act
        var result = PrimeFinder.NextPrime(desiredCapacity);

        // Assert
        Assert.True(result >= desiredCapacity);
    }

    [Fact]
    public void Init_ShouldNotThrow_WhenCalledMultipleTimes()
    {
        // Arrange & Act & Assert - should not throw
        PrimeFinder.Init();
        PrimeFinder.Init();
        PrimeFinder.Init();
    }

    [Fact]
    public void NextPrime_ShouldThrowOverflowException_WhenCapacityExceedsMaximum()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = int.MaxValue;

        // Act & Assert
        var exception = Assert.Throws<OverflowException>(() => PrimeFinder.NextPrime(desiredCapacity));
        Assert.Contains("exceeds maximum available prime", exception.Message);
    }
}

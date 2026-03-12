
using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class PrimeFinderTests
{
    [Test]
    public async Task NextPrime_ShouldReturnNextPrime_WhenCapacityIsNotPrime()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = 100;

        // Act
        var result = PrimeFinder.NextPrime(desiredCapacity);

        // Assert
        await Assert.That(result).IsGreaterThanOrEqualTo(101);
    }

    [Test]
    public async Task NextPrime_ShouldReturnLargerPrime_WhenCapacityIsPrime()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = 97;

        // Act
        var result = PrimeFinder.NextPrime(desiredCapacity);

        // Assert
        await Assert.That(result).IsGreaterThanOrEqualTo(98);
        await Assert.That(result).IsNotEqualTo(97);
    }

    [Test]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(50)]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task NextPrime_ShouldReturnValidPrime_WhenGivenVariousCapacities(int capacity)
    {
        // Arrange
        PrimeFinder.Init();

        // Act
        var result = PrimeFinder.NextPrime(capacity);

        // Assert
        await Assert.That(result).IsGreaterThanOrEqualTo(capacity);
    }

    [Test]
    public async Task NextPrime_ShouldReturnAtLeastGivenCapacity()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = 1000;

        // Act
        var result = PrimeFinder.NextPrime(desiredCapacity);

        // Assert
        await Assert.That(result >= desiredCapacity).IsTrue();
    }

    [Test]
    public void Init_ShouldNotThrow_WhenCalledMultipleTimes()
    {
        // Arrange & Act & Assert - should not throw
        PrimeFinder.Init();
        PrimeFinder.Init();
        PrimeFinder.Init();
    }

    [Test]
    public async Task NextPrime_ShouldThrowOverflowException_WhenCapacityExceedsMaximum()
    {
        // Arrange
        PrimeFinder.Init();
        var desiredCapacity = int.MaxValue;

        // Act & Assert
        var exception = Assert.Throws<OverflowException>(() => PrimeFinder.NextPrime(desiredCapacity));
        await Assert.That(exception.Message).Contains("exceeds maximum available prime");
    }
}

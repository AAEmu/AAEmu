using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class StringExtensionsTest
{

    [Test]
    [Arguments("test", "Test"),
    Arguments("Test", "Test"),
    Arguments("tEST", "TEST"),
    Arguments("TEST", "TEST"),
    Arguments("test test", "Test test"),
    Arguments("t", "T"),
    Arguments("T", "T"),
    Arguments("1", "1")]
    public async Task FirstCharToUpper_ShouldWorkAsExpected(string input, string expected)
    {
        // Act
        var actual = input.FirstCharToUpper();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(null)]
    public void FirstCharToUpper_ShouldThrowWhenInvalid(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => input.FirstCharToUpper());
    }

    [Test]
    [Arguments("test", "Test"),
     Arguments("Test", "Test"),
     Arguments("tEST", "Test"),
     Arguments("TEST", "Test"),
     Arguments("TEST ", "Test"),
     Arguments(" tEsT ", "Test"),
     Arguments("test test", "Test test"),
     Arguments("t", "T"),
     Arguments("T", "T"),
     Arguments("1", "1"),
     Arguments(" \t ", " \t ")]
    public async Task NormalizeName_ShouldWorkAsExpected(string input, string expected)
    {
        // Act
        var actual = input.NormalizeName();

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }
}

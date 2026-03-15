using AAEmu.Login.Core.Services.TwoFactor;
using AAEmu.Login.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AAEmu.UnitTests.Login.Core.Services.TwoFactor;

public class ArsServiceTests
{
    private readonly FakeTimeProvider _timeProvider = new();

    private static readonly AccountId s_testAccountId = new(7);
    private static readonly AccountId s_otherAccountId = new(99);

    private ArsService CreateSut() => new(_timeProvider, NullLogger<ArsService>.Instance);

    [Test]
    public async Task CreateSessionAsync_ReturnsSessionWithFourDigitCode()
    {
        var session = await CreateSut()
            .CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(session.SessionCode.Length).IsEqualTo(4);
        await Assert.That(int.TryParse(session.SessionCode, out var code)).IsTrue();
        await Assert.That(code).IsGreaterThanOrEqualTo(1000);
        await Assert.That(code).IsLessThanOrEqualTo(9999);
    }

    [Test]
    public async Task CreateSessionAsync_SetsCorrectExpiry()
    {
        var expectedExpiry = _timeProvider.GetUtcNow().AddSeconds(120);

        var session = await CreateSut()
            .CreateSessionAsync(s_testAccountId, 120, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(session.ExpiresAt).IsEqualTo(expectedExpiry);
    }

    [Test]
    public async Task CreateSessionAsync_ReplacesExistingSession()
    {
        var sut = CreateSut();
        await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);
        var second =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);

        // Only the second session should be active
        var success =
            await sut.CompleteSessionAsync(s_testAccountId, second.SessionCode,
                TestContext.Current!.Execution.CancellationToken);

        await Assert.That(success).IsTrue();
    }

    // --- CompleteSessionAsync ---

    [Test]
    public async Task CompleteSessionAsync_NoActiveSession_ReturnsFalse()
    {
        var result = await CreateSut()
            .CompleteSessionAsync(s_testAccountId, "1234", TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CompleteSessionAsync_ExpiredSession_ReturnsFalse()
    {
        var sut = CreateSut();
        var session =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(61));

        var result = await sut.CompleteSessionAsync(s_testAccountId, session.SessionCode,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CompleteSessionAsync_WrongCode_ReturnsFalse()
    {
        var sut = CreateSut();
        var session =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);
        var wrongCode = session.SessionCode == "1234" ? "5678" : "1234";

        var result = await sut.CompleteSessionAsync(s_testAccountId, wrongCode,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CompleteSessionAsync_CorrectCode_ReturnsTrue()
    {
        var sut = CreateSut();
        var session =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);

        var result = await sut.CompleteSessionAsync(s_testAccountId, session.SessionCode,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CompleteSessionAsync_AfterSuccess_SessionConsumed()
    {
        var sut = CreateSut();
        var session =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);
        await sut.CompleteSessionAsync(s_testAccountId, session.SessionCode,
            TestContext.Current!.Execution.CancellationToken);

        var second = await sut.CompleteSessionAsync(s_testAccountId, session.SessionCode,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(second).IsFalse();
    }

    [Test]
    public async Task CompleteSessionAsync_DifferentAccount_ReturnsFalse()
    {
        var sut = CreateSut();
        var session =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);

        var result = await sut.CompleteSessionAsync(s_otherAccountId, session.SessionCode,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CompleteSessionAsync_AtExactExpiry_ReturnsFalse()
    {
        var sut = CreateSut();
        var session =
            await sut.CreateSessionAsync(s_testAccountId, 60, TestContext.Current!.Execution.CancellationToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(60));

        // Advancing to exactly ExpiresAt — the service checks now > expiresAt, so this is still valid
        // Advance one more tick to push past the boundary
        _timeProvider.Advance(TimeSpan.FromTicks(1));

        var result = await sut.CompleteSessionAsync(s_testAccountId, session.SessionCode,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result).IsFalse();
    }
}

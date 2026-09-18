using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.SecondPassword;
using AAEmu.Game.Models.Game.SensitiveOperation;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

using Microsoft.Extensions.Time.Testing;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The account-protection window driven through the guard itself: what a refused action does to the window,
/// what the client's account-protection packet (CS 0x19A) may and may not do to it, and what lifts it.
/// </summary>
/// <remarks>
/// Process-wide state — the feature set, the guard's clock, the guard's windows and the second-password
/// manager — is swapped per test and put back afterwards, so this class runs on its own.
/// </remarks>
[NotInParallel]
public sealed class SensitiveOperationGuardTests
{
    private const uint CharacterId = 61_621;

    private static readonly PropertyInfo FsetsProperty = typeof(FeaturesManager).GetProperty(
        nameof(FeaturesManager.Fsets), BindingFlags.Public | BindingFlags.Static)!;

    private static int _nextAccountId = 6_162_000;

    /// <summary>
    /// The account this test drives. The guard's windows outlive a test, so each one gets an account of its
    /// own rather than finding whatever the previous test left behind.
    /// </summary>
    private readonly uint _accountId = (uint)Interlocked.Increment(ref _nextAccountId);

    private readonly List<byte[]> _sent = [];
    private readonly FakeTimeProvider _clock =
        new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly FeatureSet _previousFeatureSet;
    private readonly SingletonScope<SecondPasswordManager> _secondPasswords;
    private readonly GameConnection _connection;
    private readonly Character _character;

    public SensitiveOperationGuardTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));

        _connection = new GameConnection(session.Object) { AccountId = _accountId };
        _character = new Character(new UnitCustomModelParams()) { Id = CharacterId, Name = "Protected" };
        _character.Connection = _connection;
        _connection.ActiveChar = _character;

        _previousFeatureSet = ReadFeatureSet();
        SetFeatureSet(FeatureSetWith(Feature.sensitiveOpeartion, true));
        SensitiveOperationGuard.Clock = _clock;

        _secondPasswords = new SingletonScope<SecondPasswordManager>(
            new SecondPasswordManager(new InMemorySecondPasswordStore()));
    }

    [After(Test)]
    public void RestoreProcessState()
    {
        SensitiveOperationGuard.Clock = TimeProvider.System;
        SetFeatureSet(_previousFeatureSet);
        _secondPasswords?.Dispose();
    }

    [Test]
    public async Task RefusedAction_LeavesTheWindowWhereItWas()
    {
        OpenWindow();

        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).RemainSeconds).IsEqualTo(600u);

        // Three blocked attempts spread over the window. A refusal that opened a fresh window would report a
        // full ten minutes again each time and the account could be kept protected forever.
        await RefuseAfter(TimeSpan.FromMinutes(1));
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).RemainSeconds).IsEqualTo(540u);

        await RefuseAfter(TimeSpan.FromMinutes(5));
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).RemainSeconds).IsEqualTo(240u);

        await RefuseAfter(TimeSpan.FromMinutes(3));
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).RemainSeconds).IsEqualTo(60u);

        await Assert.That(SensitiveOperationGuard.Describe(_character)).Contains("protected for another 60s");
    }

    [Test]
    public async Task RefusedAction_IsAllowedOnceTheWindowLapses()
    {
        OpenWindow();

        // Still refused a minute before the window runs out, and through again once it has.
        await RefuseAfter(TimeSpan.FromMinutes(9));
        _clock.Advance(TimeSpan.FromMinutes(2));

        await Assert.That(SensitiveOperationGuard.MayPerform(_character, SensitiveOperationKind.Mail,
            out var reason)).IsTrue();
        await Assert.That(reason).IsNull();
    }

    [Test]
    public async Task ProtectStateRequest_CannotOpenAWindow()
    {
        var mark = _sent.Count;

        QueryClient();

        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).Protected).IsFalse();
        await AssertStateAnswer(mark, protectedFlag: 0, remainSeconds: 0u);
    }

    [Test]
    public async Task ProtectStateRequest_CannotLiftAWindow()
    {
        OpenWindow();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var mark = _sent.Count;

        QueryClient();

        // The answer reports the window and the window is still there, with the countdown it had: the packet
        // is a query and the only thing that lifts a window is a verified second password.
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).Protected).IsTrue();
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).RemainSeconds).IsEqualTo(540u);
        await AssertStateAnswer(mark, protectedFlag: 1, remainSeconds: 540u);
    }

    [Test]
    [Arguments((byte)0)]
    [Arguments((byte)1)]
    public async Task ProtectStateRequest_WithACraftedBody_CannotOpenAWindow(byte bodyByte)
    {
        var mark = _sent.Count;

        QueryClient(bodyByte);

        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).Protected).IsFalse();
        await AssertStateAnswer(mark, protectedFlag: 0, remainSeconds: 0u);
    }

    [Test]
    [Arguments((byte)0)]
    [Arguments((byte)1)]
    public async Task ProtectStateRequest_WithACraftedBody_CannotLiftAWindow(byte bodyByte)
    {
        OpenWindow();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var mark = _sent.Count;

        // A zero byte here used to be read as "stop protecting me" and dropped the window without any
        // verification. Nothing in a body may move it now.
        QueryClient(bodyByte);

        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).Protected).IsTrue();
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).RemainSeconds).IsEqualTo(540u);
        await AssertStateAnswer(mark, protectedFlag: 1, remainSeconds: 540u);
    }

    [Test]
    public async Task AcceptedSecondPassword_LiftsTheWindowAndTellsTheClient()
    {
        OpenWindow();
        var mark = _sent.Count;

        SensitiveOperationGuard.OnSecondPasswordVerified(_character);

        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).Protected).IsFalse();
        await Assert.That(SensitiveOperationGuard.MayPerform(_character, SensitiveOperationKind.Trade,
            out _)).IsTrue();
        await Assert.That(SentOpcode(mark)).IsEqualTo(SCOffsets.SCSensitiveOperationVerifySuccessPacket);
    }

    [Test]
    public async Task AWindow_IsRefusedWithoutASecondPasswordToLiftIt()
    {
        var mark = _sent.Count;

        var opened = SensitiveOperationGuard.TrySetProtection(_character, protect: true, out var refusal);

        await Assert.That(opened).IsFalse();
        await Assert.That(refusal).Contains("second password");
        await Assert.That(SensitiveOperationGuard.StateFor(_accountId, Now).Protected).IsFalse();
        await Assert.That(_sent.Count).IsEqualTo(mark);
    }

    [Test]
    public async Task Guard_IsInertWhileTheFeatureBitIsOff()
    {
        OpenWindow();
        SetFeatureSet(FeatureSetWith(Feature.sensitiveOpeartion, false));
        var mark = _sent.Count;

        QueryClient();

        // Nothing is held back and nothing is answered: this is the shipped configuration, and the window is
        // left where it was for the guard to forget when it lapses.
        await Assert.That(SensitiveOperationGuard.MayPerform(_character, SensitiveOperationKind.ItemDestruction,
            out var reason)).IsTrue();
        await Assert.That(reason).IsNull();
        await Assert.That(_sent.Count).IsEqualTo(mark);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private void OpenWindow()
    {
        SecondPasswordManager.Instance.TryCreate(_accountId, "1234");
        if (!SensitiveOperationGuard.TrySetProtection(_character, protect: true, out var refusal))
            throw new InvalidOperationException($"the window was refused: {refusal}");
    }

    private async Task RefuseAfter(TimeSpan delay)
    {
        _clock.Advance(delay);

        var allowed = SensitiveOperationGuard.MayPerform(_character, SensitiveOperationKind.ItemDestruction,
            out var reason);

        await Assert.That(allowed).IsFalse();
        await Assert.That(reason).IsNotNull();
    }

    private void QueryClient(params byte[] body) =>
        new CSProtectSensitiveOperation { Connection = _connection }.Read(new PacketStream(body));

    private async Task AssertStateAnswer(int mark, byte protectedFlag, uint remainSeconds)
    {
        await Assert.That(_sent.Count).IsEqualTo(mark + 1);

        var (opcode, body) = ReadSent(_sent[mark]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCProtectSensitiveOperationResultPacket);

        var stream = new PacketStream(body);
        await Assert.That(stream.ReadByte()).IsEqualTo(protectedFlag);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(remainSeconds);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    private ushort SentOpcode(int index) => ReadSent(_sent[index]).Opcode;

    /// <summary>One captured packet: its opcode and its body, past the unencrypted game envelope.</summary>
    private static (ushort Opcode, byte[] Body) ReadSent(IReadOnlyList<byte> packet)
    {
        var stream = new PacketStream(packet.ToArray());
        stream.ReadUInt16(); // envelope length
        stream.ReadByte();   // signature
        var level = stream.ReadByte();
        if (level == 1)
        {
            stream.ReadByte(); // checksum
            stream.ReadByte(); // counter
        }

        return (stream.ReadUInt16(), stream.ReadBytes(stream.LeftBytes));
    }

    private static FeatureSet ReadFeatureSet() => (FeatureSet)FsetsProperty.GetValue(null)!;

    private static void SetFeatureSet(FeatureSet fsets) => FsetsProperty.SetValue(null, fsets);

    private static FeatureSet FeatureSetWith(Feature feature, bool enabled)
    {
        var fsets = new FeatureSet();
        if (!fsets.Set(feature, enabled))
            throw new InvalidOperationException($"{feature} is not an addressable feature bit");
        return fsets;
    }

    private sealed class InMemorySecondPasswordStore : ISecondPasswordStore
    {
        private readonly Dictionary<uint, SecondPasswordRecord> _records = [];

        public SecondPasswordRecord Load(uint accountId) =>
            _records.TryGetValue(accountId, out var record) ? record : null;

        public void Save(SecondPasswordRecord record) => _records[record.AccountId] = record;

        public void Delete(uint accountId) => _records.Remove(accountId);
    }
}

using System.Net;
using System.Net.Sockets;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Families;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The client-to-game packets whose handler writes state from inside <c>Read</c> and whose default
/// value for a short read is itself a legal value: a truncated body used to write that default
/// straight over good state, and the write was already done by the time anything noticed.
/// </summary>
/// <remarks>
/// Every case runs the <em>complete</em> body first and shows the write happening, then the
/// truncated body and shows the state unchanged. Without the first half, a case would pass just as
/// happily against a handler it never reached.
/// </remarks>
[NotInParallel]
public class TruncatedBodyStateMutationTests
{
    private const string Kept = "keep me";
    private const string Written = "written by the client";
    private const uint OwnerCharacterId = 1;
    private const uint MemberCharacterId = 2;

    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;
        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    private sealed class FamilyFixture : IDisposable
    {
        public Family Family { get; }
        public FamilyMember Member { get; }
        public int Saves { get; private set; }
        public SingletonScope<FamilyManager> Scope { get; }

        public FamilyFixture(Character owner, RecordingSession wire)
        {
            // FamilyManager looks the family up by owner.Family, not by the id on the Family object,
            // so both characters have to carry it or even a well formed body is refused.
            owner.Family = OwnerCharacterId;
            var member = new CharacterMock { Id = MemberCharacterId, Name = "Member" };
            member.Family = OwnerCharacterId;
            member.Connection = new GameConnection(new RecordingSession()) { ActiveChar = member };

            Family = new Family { Id = OwnerCharacterId };
            Family.AddMember(new FamilyMember
            {
                Id = owner.Id, Character = owner, Name = owner.Name, Role = 1, Title = Kept,
            });
            Member = new FamilyMember
            {
                Id = member.Id, Character = member, Name = member.Name, Role = 0, Title = Kept,
            };
            Family.AddMember(Member);

            var manager = new FamilyManager(Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object,
                Mock.Of<IFamilyIdManager>().Object, Mock.Of<IFamilyPurchaseService>().Object,
                _ => Saves++, isCurrentSession: _ => true);
            SetField(manager, "_families", new Dictionary<uint, Family> { [Family.Id] = Family });
            SetField(manager, "_familyMembers", Family.Members.ToDictionary(x => x.Id));
            Scope = new SingletonScope<FamilyManager>(manager);
            OwnerWire = wire;
        }

        public RecordingSession OwnerWire { get; }

        public void Dispose() => Scope.Dispose();
    }

    // ------------------------------------------------------------------ family notice

    [Test]
    public async Task FamilyNoticeSet_CompleteBody_WritesTheNotice()
    {
        var (owner, wire) = Owner();
        using var fixture = new FamilyFixture(owner, wire);
        fixture.Family.Notice = Kept;

        var threw = Read(new CSFamilyNoticeSetPacket { Connection = owner.Connection },
            new PacketStream().Write(Written, appendSize: true));

        await Assert.That(threw).IsNull();
        await Assert.That(fixture.Family.Notice).IsEqualTo(Written);
        await Assert.That(fixture.Saves).IsEqualTo(1);
        await Assert.That(CountOf(fixture.OwnerWire, SCOffsets.SCFamilyDescPacket)).IsGreaterThan(0);
    }

    [Test]
    public async Task FamilyNoticeSet_TruncatedBody_LeavesTheNoticeIntact()
    {
        var (owner, wire) = Owner();
        using var fixture = new FamilyFixture(owner, wire);
        fixture.Family.Notice = Kept;

        // The u16 length promises eight bytes and none of them arrive, so the string read degrades to
        // "" - a value FamilyProgressionRules.IsValidNotice accepts, which is the whole defect.
        var threw = Read(new CSFamilyNoticeSetPacket { Connection = owner.Connection }, TruncatedString());

        await Assert.That(threw).IsTypeOf<TruncatedPacketException>();
        await Assert.That(fixture.Family.Notice).IsEqualTo(Kept);
        await Assert.That(fixture.Saves).IsEqualTo(0);
        await Assert.That(CountOf(fixture.OwnerWire, SCOffsets.SCFamilyDescPacket)).IsEqualTo(0);
    }

    // ------------------------------------------------------------------ family title

    [Test]
    public async Task FamilyChangeTitle_CompleteBody_WritesTheMemberTitle()
    {
        var (owner, wire) = Owner();
        using var fixture = new FamilyFixture(owner, wire);

        var threw = Read(new CSFamilyChangeTitlePacket { Connection = owner.Connection }, new PacketStream()
            .Write((ulong)MemberCharacterId)
            .Write(Written, appendSize: true));

        await Assert.That(threw).IsNull();
        await Assert.That(fixture.Member.Title).IsEqualTo(Written);
        await Assert.That(fixture.Saves).IsEqualTo(1);
        await Assert.That(CountOf(fixture.OwnerWire, SCOffsets.SCFamilyTitleChangedPacket)).IsGreaterThan(0);
    }

    [Test]
    public async Task FamilyChangeTitle_TruncatedBody_LeavesTheMemberTitleIntact()
    {
        var (owner, wire) = Owner();
        using var fixture = new FamilyFixture(owner, wire);

        // The member id arrives; the title's declared length does not.
        var body = new PacketStream()
            .Write((ulong)MemberCharacterId)
            .Write((ushort)8);
        var threw = Read(new CSFamilyChangeTitlePacket { Connection = owner.Connection }, body);

        await Assert.That(threw).IsTypeOf<TruncatedPacketException>();
        await Assert.That(fixture.Member.Title).IsEqualTo(Kept);
        await Assert.That(fixture.Saves).IsEqualTo(0);
        await Assert.That(CountOf(fixture.OwnerWire, SCOffsets.SCFamilyTitleChangedPacket)).IsEqualTo(0);
    }

    // ------------------------------------------------------------------ expedition notice

    [Test]
    public async Task ExpeditionNoticeUpdate_TruncatedBody_LeavesTheNoticeIntact()
    {
        // The premise, pinned first: this is why an empty default is destructive on the guild side
        // too. ExpeditionTextRules accepts "" exactly as the family rules do, so nothing downstream
        // refuses a truncated read.
        await Assert.That(ExpeditionTextRules.IsValidNotice(string.Empty)).IsTrue();

        var (owner, _) = Owner();
        var expedition = Guild(owner);
        using var scope = new SingletonScope<ExpeditionManager>(ExpeditionManagerFor(owner));

        // The type field arrives, the notice's declared length does not.
        var threw = Read(new CSExpeditionNoticeUpatePacket { Connection = owner.Connection },
            new PacketStream().Write(0).Write((ushort)8));

        await Assert.That(threw).IsTypeOf<TruncatedPacketException>();
        await Assert.That(expedition.Notice).IsEqualTo(Kept);
    }

    [Test]
    public async Task ExpeditionNoticeUpdate_CompleteBody_IsNotRefusedByTheGate()
    {
        var (owner, _) = Owner();
        Guild(owner);
        using var scope = new SingletonScope<ExpeditionManager>(ExpeditionManagerFor(owner));

        // The negative control for the gate. ExpeditionManager.SetNotice persists through a
        // MySqlConnection, so the stored row cannot be observed without a database; what this pins is
        // that a whole body passes the gate and reaches the handler, which a blanket refusal breaks.
        var packet = new CSExpeditionNoticeUpatePacket { Connection = owner.Connection };
        var threw = Read(packet, new PacketStream()
            .Write(0)
            .Write(Written, appendSize: true));

        await Assert.That(threw is IOException).IsFalse(); // the read-level gate did not fire
        await Assert.That(packet.Notice).IsEqualTo(Written);
    }

    // ------------------------------------------------------------------ recruitment introduction

    [Test]
    public async Task ExpeditionRecruitmentAdd_CompleteBody_ReachesTheService()
    {
        // The negative control: a whole body gets past the guard and as far as the service lookup.
        // Driven on a lenient stream, because that is the caller the guard exists for - with the
        // dispatch arming strict reads the stream stops first.
        //
        // No service provider is installed, so the lookup itself throws. That throw is the evidence
        // the guard let the body through: the packet reached the line that resolves the service. Doing
        // it this way rather than swapping SingletonContainer.ServiceProvider keeps the check
        // process-local - a provider swap is visible to every other class in the run.
        var owner = Owner().Character;
        Guild(owner);
        var packet = new CSExpeditionRecruitmentAddPacket { Connection = owner.Connection };

        var threw = Read(packet, new PacketStream()
            .Write((short)0)
            .Write(0u)
            .Write(Written, appendSize: true), strict: false);

        await Assert.That(threw is InvalidDataException).IsFalse();
        await Assert.That(packet.Introduce).IsEqualTo(Written);
    }

    [Test]
    public async Task ExpeditionRecruitmentAdd_TruncatedBody_NeverReachesTheService()
    {
        var owner = Owner().Character;
        Guild(owner);
        var packet = new CSExpeditionRecruitmentAddPacket { Connection = owner.Connection };

        var threw = Read(packet, TruncatedString(), strict: false);

        // The guard's own refusal, not the service lookup's: Register upserts and would overwrite the
        // guild's introduction and restart its expiry, and both a zeroed day and an empty
        // introduction pass its own validation.
        await Assert.That(threw).IsTypeOf<InvalidDataException>();
        await Assert.That(packet.Introduce).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ExpeditionRecruitmentAdd_TruncatedBody_IsAlsoStoppedByTheFrameworkGate()
    {
        // The same body with strict reads armed, which is what the dispatch does: the stream stops at
        // the day field before the handler guard is ever reached.
        var owner = Owner().Character;
        Guild(owner);
        var packet = new CSExpeditionRecruitmentAddPacket { Connection = owner.Connection };

        var threw = Read(packet, TruncatedString(), strict: true);

        await Assert.That(threw).IsTypeOf<TruncatedPacketException>();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Runs a packet the way the dispatch does - arm the body, then Decode - and returns whatever came
    /// out, or null when the packet completed. Naming the exception type is what tells "refused by
    /// the parse" apart from "reached the handler and then failed for an unrelated reason":
    /// <see cref="TruncatedPacketException"/> is the stream stopping at the offending byte, a plain
    /// <see cref="InvalidDataException"/> is a per-handler guard refusing the body.
    /// </summary>
    /// <param name="strict">
    /// false leaves the body lenient, which is how a per-handler guard is exercised: with the
    /// dispatch arming strict reads the stream stops first, so the guard is unreachable in
    /// production. It still has to hold on its own for any caller that does not arm the stream.
    /// </param>
    private static Exception Read(GamePacket packet, PacketStream body, bool strict = true)
    {
        body.RequireComplete(strict && packet.RequiresCompleteBody);
        try
        {
            packet.Decode(body);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>A u16 length that promises eight bytes with none of them present.</summary>
    private static PacketStream TruncatedString() => new PacketStream().Write((ushort)8);

    private static (Character Character, RecordingSession Wire) Owner()
    {
        var wire = new RecordingSession();
        var character = new CharacterMock { Id = OwnerCharacterId, Name = "Owner" };
        character.Connection = new GameConnection(wire) { ActiveChar = character };
        return (character, wire);
    }

    private static ExpeditionManager ExpeditionManagerFor(Character owner)
    {
        var world = Mock.Of<IWorldManager>();
        world.GetCharacterById(owner.Id).Returns(owner);
        return new ExpeditionManager(
            Mock.Of<IExpeditionIdManager>().Object, Mock.Of<ITeamManager>().Object, world.Object,
            Mock.Of<IChatManager>().Object, Mock.Of<IExpeditionPersistenceConnectionFactory>().Object,
            Mock.Of<IItemManager>().Object);
    }

    private static Expedition Guild(Character owner)
    {
        var id = (FactionsEnum)500;
        var expedition = new Expedition
        {
            Id = id,
            Name = "Guild",
            Notice = Kept,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            Members =
            [
                new ExpeditionMember
                {
                    ExpeditionId = id, CharacterId = owner.Id, Name = owner.Name, Role = byte.MaxValue,
                },
            ],
        };
        owner.Expedition = expedition;
        return expedition;
    }

    private static void SetField<T>(object target, string name, T value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static int CountOf(RecordingSession wire, ushort typeId) =>
        wire.Packets.Count(bytes =>
        {
            var stream = new PacketStream(bytes);
            stream.ReadUInt16(); // length
            stream.ReadByte();   // signature
            if (stream.ReadByte() == 1) // level
            {
                stream.ReadByte(); // hash
                stream.ReadByte(); // counter
            }
            return stream.ReadUInt16() == typeId;
        });
}

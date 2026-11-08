using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Scripts;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class NymphQueenAbandonTests
{
    [Test]
    public async Task RefreshAbandonedQuest_UnturnedInQuest_ResetsClientWithoutChangingOtherCompletions()
    {
        var session = new RecordingSession();
        var owner = new Character(null) { Connection = new GameConnection(session) };
        var quests = new CharacterQuests(owner);
        owner.Quests = quests;
        quests.SetCompletedQuestFlag(2007, true);

        CreateManager().NotifyQuestRemoved(CreateQuest(owner, 2008), true);

        await Assert.That(session.Packets.Count).IsEqualTo(1);
        // The reset body ends with quest ID, the eight-byte completion mask and block ID.
        var body = session.Packets[0].AsSpan(session.Packets[0].Length - 16).ToArray();
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(2008u);
        var mask = BitConverter.ToUInt64(body, 4);
        await Assert.That((mask & (1UL << (2008 % 64))) == 0).IsTrue();
        await Assert.That((mask & (1UL << (2007 % 64))) != 0).IsTrue();
        await Assert.That(BitConverter.ToUInt32(body, 12)).IsEqualTo(2008u / 64);
        await Assert.That(quests.HasQuestCompleted(2007)).IsTrue();
        await Assert.That(quests.HasQuestCompleted(2008)).IsFalse();
    }

    [Test]
    [Arguments(2008u, false, false)]
    [Arguments(2008u, true, true)]
    [Arguments(2007u, true, false)]
    public async Task RefreshAbandonedQuest_TurnInOrUnrelatedQuest_DoesNotResetClient(uint questId, bool update, bool completed)
    {
        var session = new RecordingSession();
        var owner = new Character(null) { Connection = new GameConnection(session) };
        var quests = new CharacterQuests(owner);
        owner.Quests = quests;
        quests.SetCompletedQuestFlag(questId, completed);

        CreateManager().NotifyQuestRemoved(CreateQuest(owner, questId), update);

        await Assert.That(session.Packets.Count).IsEqualTo(0);
        await Assert.That(quests.HasQuestCompleted(questId)).IsEqualTo(completed);
    }

    private static QuestManager CreateManager()
    {
        var tasks = Mock.Of<ITaskManager>().Object;
        var script = new NymphQueenQuestScript(new Lazy<IQuestManager>(() => Mock.Of<IQuestManager>().Object),
            tasks, Mock.Of<ITeamManager>().Object, TimeProvider.System, new());
        return new QuestManager(tasks, Mock.Of<IZoneManager>().Object, [script]);
    }

    private static Quest CreateQuest(Character owner, uint id)
    {
        return new Quest(null, owner, Mock.Of<IQuestManager>().Object, Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object, Mock.Of<IExpressTextManager>().Object, Mock.Of<IWorldManager>().Object)
            { TemplateId = id };
    }

    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null;
        public void SendPacket(byte[] packet) { Packets.Add(packet); }
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) { return null; }
        public void ClearAttribute(string name) { }
        public void Close() { }
    }
}

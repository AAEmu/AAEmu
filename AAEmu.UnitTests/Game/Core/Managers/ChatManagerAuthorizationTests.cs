using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ChatManagerAuthorizationTests
{
    [Test]
    public async Task FactionRoute_RejectsSpoofedSenderAndDeliversToTheJoinedChannel()
    {
        var manager = new ChatManager();
        var sender = OnlineCharacter(1, 501);
        var valid = OnlineCharacter(2, 501);
        var stale = OnlineCharacter(3, 501);
        var channel = manager.GetFactionChat(sender);
        channel.JoinChannel(sender);
        channel.JoinChannel(valid);
        channel.JoinChannel(stale);

        // A member that has changed faction without a re-sync is still a member of the channel its
        // client was announced on, so it stays on the sender's list and still receives.
        stale.Faction = PlayerFaction(502);
        await Assert.That(manager.SendFactionMessage(sender, "allowed")).IsEqualTo(3);

        // An outsider cannot send by naming the channel's faction: only membership counts.
        var outsider = OnlineCharacter(4, 501);
        await Assert.That(manager.SendFactionMessage(outsider, "spoofed")).IsEqualTo(0);

        sender.Faction = PlayerFaction(502);
        var newChannel = manager.SyncFactionChannel(sender);
        await Assert.That(channel.Contains(sender)).IsFalse();
        await Assert.That(newChannel.Contains(sender)).IsTrue();
        await Assert.That(newChannel.Faction).IsEqualTo((FactionsEnum)502);
    }

    [Test]
    public async Task FactionRoute_FollowsTheJoinedChannelAcrossATemporaryFactionChange()
    {
        var manager = new ChatManager();
        var duelist = OnlineCharacter(1, 501);
        var peer = OnlineCharacter(2, 501);
        var channel = manager.SyncFactionChannel(duelist);
        manager.SyncFactionChannel(peer);

        // A duel swaps the faction without touching the channel: the duelist was announced on the
        // 501 channel and its client is still in it.
        duelist.Faction = PlayerFaction((uint)FactionsEnum.RedTeam);
        await Assert.That(manager.GetJoinedFactionChat(duelist)).IsSameReferenceAs(channel);

        await Assert.That(manager.SendFactionMessage(duelist, "still here")).IsEqualTo(2);
        await Assert.That(manager.GetFactionChat(duelist).MemberCount).IsEqualTo(0);

        // Someone in no faction channel at all has no channel to send on.
        var bystander = OnlineCharacter(4, 501);
        await Assert.That(manager.SendFactionMessage(bystander, "nowhere")).IsEqualTo(0);
    }

    private static Character OnlineCharacter(uint id, uint factionId)
    {
        var session = Mock.Of<ISession>();
        var character = new Character(new UnitCustomModelParams())
            { Id = id, Name = $"Character {id}", Faction = PlayerFaction(factionId) };
        character.Connection = new GameConnection(session.Object) { ActiveChar = character };
        typeof(Character).GetField("_isOnline", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, true);
        return character;
    }

    private static SystemFaction PlayerFaction(uint id) => new()
        { Id = (FactionsEnum)id, MotherId = FactionsEnum.Invalid, DiplomacyTarget = true };
}

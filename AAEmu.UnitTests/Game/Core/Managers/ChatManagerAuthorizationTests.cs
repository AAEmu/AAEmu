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
    public async Task FactionRoute_RejectsSpoofedSenderAndFiltersStaleMembers()
    {
        var manager = new ChatManager();
        var sender = OnlineCharacter(1, 501);
        var valid = OnlineCharacter(2, 501);
        var stale = OnlineCharacter(3, 501);
        var channel = manager.GetFactionChat(sender);
        channel.JoinChannel(sender);
        channel.JoinChannel(valid);
        channel.JoinChannel(stale);
        stale.Faction = PlayerFaction(502);

        var sent = manager.SendFactionMessage(sender, "allowed");
        await Assert.That(sent).IsEqualTo(2);

        var outsider = OnlineCharacter(4, 501);
        await Assert.That(manager.SendFactionMessage(outsider, "spoofed")).IsEqualTo(0);

        sender.Faction = PlayerFaction(502);
        var newChannel = manager.SyncFactionChannel(sender);
        await Assert.That(channel.Contains(sender)).IsFalse();
        await Assert.That(newChannel.Contains(sender)).IsTrue();
        await Assert.That(newChannel.Faction).IsEqualTo((FactionsEnum)502);
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

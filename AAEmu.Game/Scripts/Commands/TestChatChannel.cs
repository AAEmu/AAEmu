using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestChatChannel : ICommand
{
    public void OnLoad()
    {
        string[] name = { "testchatchannel", "test_chat_channel" };
        CommandManager.Instance.Register(name, this);
    }

    public string GetCommandLineHelp()
    {
        return "[list || clean || <<join||leave> chattypeid chatsubtype chatfaction>]";
    }

    public string GetCommandHelpText()
    {
        return "Command used to manually send join/leave channel packets to yourself used for testing\r" +
            "You can also use list to show a list of all current chat channels, or clean to remove any non-system channel that has zero users in it.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if ((args.Length == 1) && (args[0].ToLower() == "list"))
        {
            character.SendMessage("[TestChatChannel] List all channels");
            var channels = ChatManager.Instance.ListAllChannels();
            foreach (var c in channels)
            {
                character.SendMessage($"T:{c.chatType} ST:{c.subType} F:{c.faction} => {c.internalId} - {c.internalName} ({c.members.Count})");
            }
            character.SendMessage("[TestChatChannel] End of list");
            return;
        }

        if ((args.Length == 1) && (args[0].ToLower() == "clean"))
        {
            var removed = ChatManager.Instance.CleanUpChannels();
            character.SendMessage($"[TestChatChannel] {removed} empty channel(s) removed");
            return;
        }

        if (args.Length < 4)
        {
            character.SendMessage("[TestChatChannel] " + CommandManager.CommandPrefix + "test_chat_channel " + GetCommandLineHelp());
            return;
        }

        var chattype = (AAEmu.Game.Models.Game.Chat.ChatType)byte.Parse(args[1]);
        var chatsubtype = byte.Parse(args[2]);
        var chatfaction = uint.Parse(args[3]);

        if (args[0].ToLower() == "join")
        {
            character.SendPacket(new SCJoinedChatChannelPacket(chattype, chatsubtype, chatfaction));
        }

        if (args[0].ToLower() == "leave")
        {
            character.SendPacket(new SCLeavedChatChannelPacket(chattype, chatsubtype, chatfaction));
        }

    }
}

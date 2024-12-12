using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class TestChatChannel : ICommand
{
    public string[] CommandNames { get; set; } = ["testchatchannel", "test_chat_channel", "testchat"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<list||clean||<<join||leave> <chatTypeId> <chatSubType> <chatFaction>>";
    }

    public string GetCommandHelpText()
    {
        return "Command used to manually send join/leave channel packets to yourself used for testing\r" +
               "You can also use list to show a list of all current chat channels, or clean to remove any non-system channel that has zero users in it.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 1 && args[0].Equals("list", StringComparison.CurrentCultureIgnoreCase))
        {
            CommandManager.SendNormalText(this, messageOutput, $"List all channels");
            var channels = ChatManager.Instance.ListAllChannels();
            foreach (var c in channels)
            {
                CommandManager.SendNormalText(this, messageOutput,
                    $"T:{c.ChatType} ST:{c.SubType} F:{c.Faction} => {c.InternalId} - {c.InternalName} ({c.Members.Count})");
            }

            CommandManager.SendNormalText(this, messageOutput, $"End of list");
            return;
        }

        if (args.Length == 1 && args[0].Equals("clean", StringComparison.CurrentCultureIgnoreCase))
        {
            var removed = ChatManager.Instance.CleanUpChannels();
            CommandManager.SendNormalText(this, messageOutput, $"{removed} empty channel(s) removed");
            return;
        }

        if (args.Length < 4)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (!Enum.TryParse<ChatType>(args[1], true, out var chatType) ||
            !byte.TryParse(args[2], out var chatSubType) ||
            !Enum.TryParse<FactionsEnum>(args[1], true, out var chatFaction)
           )
        {
            CommandManager.SendErrorText(this, messageOutput, $"Parse error");
            return;
        }

        if (args[0].Equals("join", StringComparison.CurrentCultureIgnoreCase))
        {
            character.SendPacket(new SCJoinedChatChannelPacket(chatType, chatSubType, chatFaction));
        }

        if (args[0].Equals("leave", StringComparison.CurrentCultureIgnoreCase))
        {
            character.SendPacket(new SCLeavedChatChannelPacket(chatType, chatSubType, chatFaction));
        }
    }
}

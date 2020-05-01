using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestChatChannel : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "test_chat_channel", "testchatchannel" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "[join||leave] chattypeid chatsubtype chatfaction";
        }

        public string GetCommandHelpText()
        {
            return "";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 4)
            {
                character.SendMessage("[TestChatChannel] " + CommandManager.CommandPrefix + "test_chat_channel "+GetCommandLineHelp());
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
}

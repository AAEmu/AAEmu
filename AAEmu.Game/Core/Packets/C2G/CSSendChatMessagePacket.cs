using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSendChatMessagePacket : GamePacket
    {
        private byte cliLocale;
        private ChatType type;
        private short subType;
        private uint factionId;

        private string targetName;
        private string message;
        private int ability;
        private byte targetWorldId;
        private byte languageType;
        private byte[] linkType = new byte[4];
        private ushort[] start = new ushort[4];
        private ushort[] lenght = new ushort[4];
        private readonly Dictionary<int, byte[]> data = new Dictionary<int, byte[]>();
        private int[] qType = new int[4];
        private long[] itemId = new long[4];

        public CSSendChatMessagePacket() : base(CSOffsets.CSSendChatMessagePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            cliLocale =  stream.ReadByte();

            // Int64 Chat
            type = (ChatType)stream.ReadInt16();
            subType = stream.ReadInt16();
            factionId = stream.ReadUInt32();
            //---

            targetName = stream.ReadString();
            targetWorldId = stream.ReadByte();
            message = stream.ReadString();
            languageType = stream.ReadByte();
            ability = stream.ReadInt32();

            for (var i = 0; i < 4; i++)
            {
                linkType[i] = stream.ReadByte(); // linkType

                if (linkType[i] > 0)
                {
                    start[i] = stream.ReadUInt16();
                    lenght[i] = stream.ReadUInt16();
                    switch (linkType[i])
                    {
                        case 1:
                            data.TryAdd(i, stream.ReadBytes(208)); // item length = 208
                            break;
                        case 3:
                            qType[i] = stream.ReadInt32(); // qType
                            break;
                        case 4:
                            itemId[i] = stream.ReadInt64(); // itemId
                            break;
                        case 5:
                            data.TryAdd(i, stream.ReadBytes(24)); // recruit
                            break;
                        case 6:
                            data.TryAdd(i, stream.ReadBytes(32)); // squad
                            break;
                        case 7:
                            data.TryAdd(i, stream.ReadBytes(337)); // url
                            break;
                    }
                }
            }

            if (message.StartsWith("/")) // With this character, start GM commands
            {
                CommandManager.Instance.Handle(Connection.ActiveChar, message.Substring(1).Trim());
                return;
            }

            switch (type)
            {
                case ChatType.Whisper:
                    var target = WorldManager.Instance.GetCharacter(targetName);
                    var packet = new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType);
                    target?.SendPacket(packet);
                    Connection.SendPacket(packet);
                    break;
                case ChatType.White:
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType),
                        true);
                    break;
                case ChatType.Shout:
                    // TODO ...
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType),
                        true);
                    break;
                case ChatType.Trade:
                    // TODO ...
                    Connection.ActiveChar.BroadcastPacket(
                        new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType, linkType, start, lenght, data, qType, itemId), true);
                    break;
                case ChatType.Clan:
                    Connection.ActiveChar.Expedition?.SendPacket(
                        new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType));
                    break;
                case ChatType.Region:
                    WorldManager.Instance.BroadcastPacketToFaction(
                        new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType), Connection.ActiveChar.Faction.Id);
                    break;
                case ChatType.Ally:
                    WorldManager.Instance.BroadcastPacketToNation(
                        new SCChatMessagePacket(cliLocale, type, Connection.ActiveChar, message, ability, languageType), Connection.ActiveChar.Race);
                    break;
            }
        }
    }
}

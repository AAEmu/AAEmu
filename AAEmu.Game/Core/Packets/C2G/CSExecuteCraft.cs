using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExecuteCraft() : GamePacket(CSOffsets.CSExecuteCraft, 1)
{
    public override void Read(PacketStream stream)
    {
        var craftId = stream.ReadUInt32();
        var objId = stream.ReadBc();
        var count = stream.ReadInt32();

        Logger.Debug("CSExecuteCraft, craftId : {0} , objId : {1}, count : {2}", craftId, objId, count);

        var character = Connection.ActiveChar;
        if (character == null ||
            !CharacterCraft.IsValidBatchCount(count) ||
            !CraftManager.Instance.TryGetCraft(craftId, out var craft))
        {
            Logger.Warn(
                "Rejected craft request craft {0}, doodad {1}, count {2} from character {3}",
                craftId,
                objId,
                count,
                character?.Id ?? 0);
            character?.SendErrorMessage(ErrorMessageType.CraftCantActAnyMore);
            return;
        }

        character.Craft.Craft(craft, count, objId);
    }
}

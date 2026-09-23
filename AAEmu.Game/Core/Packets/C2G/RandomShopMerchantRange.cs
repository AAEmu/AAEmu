using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The same near-merchant check vendor buy uses, for a random shop opened from an NPC or a doodad.
/// </summary>
internal static class RandomShopMerchantRange
{
    public static uint ResolvePackId(Character character, uint npcObjId, uint doodadObjId)
    {
        if (character?.ParentWorld == null)
            return 0;

        if (npcObjId != 0)
        {
            var npc = character.ParentWorld.GetNpc(npcObjId);
            if (npc == null)
                return 0;
            if (MathUtil.CalculateDistance(character.Transform.World.Position, npc.Transform.World.Position) >
                CSBuyItemsPacket.MerchantInteractionRange)
            {
                character.SendErrorMessage(ErrorMessageType.TooFarAway);
                return 0;
            }

            return npc.Template?.MerchantRandomPackId ?? 0;
        }

        if (doodadObjId == 0)
            return 0;

        var doodad = character.ParentWorld.GetDoodad(doodadObjId);
        if (doodad == null)
            return 0;
        if (MathUtil.CalculateDistance(character.Transform.World.Position, doodad.Transform.World.Position) >
            CSBuyItemsPacket.MerchantInteractionRange)
        {
            character.SendErrorMessage(ErrorMessageType.TooFarAway);
            return 0;
        }

        return RandomMerchantGameData.Instance.GetPackIdForDoodad(doodad.TemplateId);
    }
}

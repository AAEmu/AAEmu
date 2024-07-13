using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncDevote : DoodadFuncTemplate
{
    // doodad_funcs
    public int Count { get; set; }
    public int ItemCount { get; set; }
    public uint ItemId { get; set; }

    private const uint AxisMundiPioneerToken = 39368u;

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Debug($"DoodadFuncDevote: Count={Count}, ItemCount={ItemCount}, ItemId={ItemId}");

        var character = (Character)caster;
        if (character == null)
            return;

        if (character.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadInteraction, ItemId, ItemCount, null) <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            owner.ToNextPhase = false;
            return;
        }

        // сохраним инфу о doodad.Data в базе
        owner.Data += ItemCount;

        // если это база на o_shining_shore_1 или o_shining_shore_2
        if (character.Transform.ZoneId == 282 || character.Transform.ZoneId == 301)
        {
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CraftActSaved, AxisMundiPioneerToken, 1);
            if (owner.Data >= Count)
            {
                owner.Data = 0;
                SaveDoodadData(owner);
                character.SendPacket(new SCDoodadChangedPacket(owner.ObjId, owner.Data));
                owner.ToNextPhase = true;
                return;
            }
        }
        else
        {
            // если это Residents
            ResidentManager.Instance.UpdateResidentTokenCount(character, owner.Data);
            if (owner.Data >= Count)
            {
                owner.Data = 0;
                SaveDoodadData(owner);
                character.SendPacket(new SCDoodadChangedPacket(owner.ObjId, owner.Data));
                ResidentManager.Instance.UpdateDevelopmentStage(character);
                owner.ToNextPhase = true;
                return;
            }
        }

        SaveDoodadData(owner);
        character.SendPacket(new SCDoodadChangedPacket(owner.ObjId, owner.Data));
        owner.ToNextPhase = false;
    }

    private static void SaveDoodadData(Doodad owner)
    {
        var persistent = owner.IsPersistent;
        if (!owner.IsPersistent)
            owner.IsPersistent = true;
        owner.Save();
        owner.IsPersistent = persistent;
    }
}

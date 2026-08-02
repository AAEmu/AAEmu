using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Confirms one item-driven phase option exposed by the target doodad's current phase.
/// </summary>
/// <remarks>
/// u32 <c>selectItemType</c>, u32 <c>selectNeedCount</c>, u32 <c>selectSkillType</c>.
/// </remarks>
public class CSDoodadItemChangerPacket() : GamePacket(CSOffsets.CSDoodadItemChangerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var doodadObjId = stream.ReadBc();
        var selectedItemId = stream.ReadUInt32();
        var selectedItemCount = stream.ReadUInt32();
        var selectedSkillId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        var doodad = character?.ParentWorld?.GetDoodad(doodadObjId);
        if (doodad == null || !doodad.IsVisible)
            return;

        lock (doodad)
        {
            var selectedChanger = FindCurrentChanger(
                doodad, selectedItemId, selectedItemCount, selectedSkillId);
            if (selectedChanger == null)
                return;

            var skill = SkillManager.Instance.GetSkillTemplate(selectedChanger.SkillId);
            if (skill == null)
                return;
            if (character.GetDistanceTo(doodad, true) > skill.MaxRange)
            {
                character.SendErrorMessage(ErrorMessageType.TooFarAway);
                return;
            }

            selectedChanger.Apply(character, doodad);
        }
    }

    private static DoodadFuncItemChanger FindCurrentChanger(
        Doodad doodad,
        uint selectedItemId,
        uint selectedItemCount,
        uint selectedSkillId)
    {
        foreach (var phaseFunc in doodad.CurrentPhaseFuncs)
        {
            if (phaseFunc.FuncType != nameof(DoodadFuncItemChanger) ||
                DoodadManager.Instance.GetPhaseFuncTemplate(phaseFunc.FuncId, phaseFunc.FuncType)
                    is not DoodadFuncItemChanger changer)
                continue;

            if (changer.ItemCount > 0 &&
                changer.ItemId == selectedItemId &&
                (uint)changer.ItemCount == selectedItemCount &&
                changer.SkillId == selectedSkillId)
                return changer;
        }

        return null;
    }
}

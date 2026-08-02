using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Confirms an item purchase offered by the target doodad's current function.
/// </summary>
/// <remarks>
/// <c>doodad_func_purchases.id</c>, bool <c>useAAPoint</c>. PurchaseDlgTask constructor
/// </remarks>
public class CSDoodadPurchaseItemPacket() : GamePacket(CSOffsets.CSDoodadPurchaseItemPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var doodadObjId = stream.ReadBc();
        var purchaseId = stream.ReadUInt32();
        var useAaPoint = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        var doodad = character?.ParentWorld?.GetDoodad(doodadObjId);
        if (doodad == null || !doodad.IsVisible)
            return;

        lock (doodad)
        {
            var func = doodad.CurrentFuncs?.FirstOrDefault(candidate =>
                candidate.FuncId == purchaseId &&
                candidate.FuncType == nameof(DoodadFuncPurchase));
            if (func == null ||
                DoodadManager.Instance.GetFuncTemplate(func.FuncId, func.FuncType) is not DoodadFuncPurchase purchase)
                return;

            if (!DoodadFuncPurchase.HasPermission(character, doodad, func))
                return;

            if (func.SkillId > 0)
            {
                var skill = SkillManager.Instance.GetSkillTemplate(func.SkillId);
                if (skill == null)
                    return;
                if (character.GetDistanceTo(doodad, true) > skill.MaxRange)
                {
                    character.SendErrorMessage(ErrorMessageType.TooFarAway);
                    return;
                }
            }

            if (!purchase.TryPurchase(character, useAaPoint))
                return;

            doodad.CompleteDeferredFunc(character, func);
        }
    }
}

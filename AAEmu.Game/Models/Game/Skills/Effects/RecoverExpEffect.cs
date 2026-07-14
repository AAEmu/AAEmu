using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RecoverExpEffect : EffectTemplate
{
    public bool NeedMoney { get; init; }
    public bool NeedLaborPower { get; init; }

    public bool NeedPriest { get; init; }
    // TODO: 1.2 specific field Penaltied, not sure how this is used
    // public bool Penaltied { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character player)
            return;
        Logger.Debug($"Player {player.Name}");
        if (player.RecoverableExp <= 0)
        {
            player.SendErrorMessage(ErrorMessageType.CannotRecoverAllNotEnough); // Is this one correct?
            return;
        }

        // TODO: Verify this formula
        var neededLaborCost = player.Level <= 50 ? player.Level : 50 + ((player.Level - 50) * 20);
        if (NeedLaborPower && player.LaborPower < neededLaborCost)
        {
            player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return;
        }

        if (NeedMoney)
        {
            // TODO: Check what this actually does if it's enabled.
            // Not used in 1.2
        }

        // Check for nearby priest if needed (caster and target are always the player)
        if (NeedPriest)
        {
            var npcs = WorldManager.GetAround<Npc>(player, 10f);
            var found = false;
            foreach (var npc in npcs)
            {
                if (npc.Template.Priest)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                player.SendErrorMessage(ErrorMessageType.TooFarAway);
                return;
            }
        }

        // Use labor and recover exp
        // Note we don't use player.ChangeLabor here as that would generate extra exp from labor consumption
        player.LaborPower -= (short)neededLaborCost;
        player.SendPacket(new SCCharacterLaborPowerChangedPacket(-neededLaborCost, 0, 0, 0));
        player.SendPacket(new SCRecoverableExpPacket(player.ObjId, 0, 0, 1));
        player.AddExp(player.RecoverableExp, false);
        player.RecoverableExp = 0;
        player.LastExpLoss = 0;
    }
}

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Blink : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Blink;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is Character)
            Logger.Debug($"Special effects: Blink value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}");

        if (caster is not Character character)
            return;

        using var newPos = character.Transform.CloneDetached();
        newPos.Local.AddDistanceToFront(value1);
        var dest = newPos.World.Position;

        if (character.IsRiding)
        {
            var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
            foreach (var mate in mateList)
                character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
        }

        character.SetPosition(dest.X, dest.Y, dest.Z,
            character.Transform.World.Rotation.X,
            character.Transform.World.Rotation.Y,
            character.Transform.World.Rotation.Z);
        character.SendPacket(new SCBlinkUnitPacket(caster.ObjId, value1, value2, dest.X, dest.Y, dest.Z));

        if (WorldIntegration.ZoneAuthority)
        {
            // baseUnitId = self for absolute blink; move3D when value3 requests vertical.
            WorldIntegration.RelayBlinkToZone?.Invoke(
                character.ObjId, character.ObjId, value3 != 0, dest.X, dest.Y, dest.Z);
        }
    }
}

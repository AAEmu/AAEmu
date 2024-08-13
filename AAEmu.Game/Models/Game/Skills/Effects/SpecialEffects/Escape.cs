using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Escape : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Escape;
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
        
        if (caster is Character character)
        {
            Logger.Debug("Special effects: Escape value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
            
            var portal = new Portal();
            
            if (WorldManager.DefaultInstanceId != character.InstanceId)
            {
                // var portal = PortalManager.Instance.GetClosestReturnPortal(character);
                // character.Transform.World;
                var w = WorldManager.Instance.GetWorld(character.InstanceId);
                if (w == null)
                {
                    return;
                }
                portal.X = w.SpawnPosition.X;
                portal.Y = w.SpawnPosition.Y;
                portal.Z = w.SpawnPosition.Z;
                portal.ZRot = w.SpawnPosition.Roll;
            }
            else
            {
                portal = PortalManager.Instance.GetClosestReturnPortal(character); 
            }
            
            // force transported out
            character.BroadcastPacket(
                new SCCharacterResurrectedPacket(
                    character.ObjId,
                    portal.X,
                    portal.Y,
                    portal.Z,
                    portal.ZRot
                ),
                true
            );
        }
    }
}

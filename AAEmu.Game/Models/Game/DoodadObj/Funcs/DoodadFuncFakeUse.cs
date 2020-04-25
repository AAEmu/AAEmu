using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFakeUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        public uint FakeSkillId { get; set; }
        public bool TargetParent { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncFakeUse : SkillId {0}, skillId {1}, FakeSkillId {2}, TargetParent {3}", SkillId, skillId, FakeSkillId, TargetParent);

            //var sound = owner.Template.FuncGroups[(int)owner.FuncGroupId].SoundId;
            var func = DoodadManager.Instance.GetFunc(owner.FuncId, skillId);
            if (func?.SoundId > 0)
            {
                owner.BroadcastPacket(new SCDoodadSoundPacket(owner, func.SoundId), true); // добавил? так как у некоторых Doodad есть звук
            }
            // added by robert
            if (func.NextPhase > 0)
            {
                owner.FuncId = (uint)func.NextPhase;
                owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), false); // FIX: added to work on/off lighting and destruction of drums/boxes
                var nextfunc = DoodadManager.Instance.GetFunc(owner.FuncId, 0);
                if (nextfunc != null)
                    nextfunc.Use(caster, owner, skillId);
            }
        }
    }
}

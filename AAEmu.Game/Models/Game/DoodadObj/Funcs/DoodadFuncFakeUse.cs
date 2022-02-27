using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFakeUse : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint SkillId { get; set; }
        public uint FakeSkillId { get; set; }
        public bool TargetParent { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncFakeUse: skillId {0}, nextPhase {1},  SkillId {2}, FakeSkillId {3}, TargetParent {4}",
                skillId, nextPhase, SkillId, FakeSkillId, TargetParent);

            if (caster == null)
            {
                return;
            }

            if (SkillId != 0)
            {
                var skillCaster = SkillCaster.GetByType(SkillCasterType.Doodad);
                skillCaster.ObjId = owner.ObjId;

                var target = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                target.ObjId = caster.ObjId;
                if (TargetParent)
                {
                    //target owner/doodad
                    target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                    target.ObjId = owner.ObjId;
                }

                var skill = new Skill(SkillManager.Instance.GetSkillTemplate(SkillId));
                skill.Use(caster, skillCaster, target);
            }
            else if (FakeSkillId != 0)
            {
                var transferTelescope = 20580;
                var range = 1000f;
                if (FakeSkillId == transferTelescope && caster is Character character)
                {
                    owner.BroadcastPacket(new SCTransferTelescopeToggledPacket(true, range), true);
                    TransferTelescopeManager.Instance.TransferTelescopeStart(character);
                }

                if (FakeSkillId == skillId && nextPhase > 0)
                {
                    var skillCaster = SkillCaster.GetByType(SkillCasterType.Doodad);
                    skillCaster.ObjId = owner.ObjId;

                    var target = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                    target.ObjId = caster.ObjId;
                    if (TargetParent)
                    {
                        //target owner/doodad
                        target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                        target.ObjId = owner.ObjId;
                    }

                    var skill = new Skill(SkillManager.Instance.GetSkillTemplate(FakeSkillId));
                    skill.Use(caster, skillCaster, target);
                }
            }
        }
    }
}

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
            _log.Debug("SkillId " + SkillId);
            _log.Debug("FakeSkillId " + FakeSkillId);
            _log.Debug("TargetParent " + TargetParent);
            _log.Debug("skillId " + skillId);
            _log.Debug("Name " + GetType().Name);
            
            if (FakeSkillId == 20580)
            {
                owner.BroadcastPacket(new SCTransferTelescopeToggledPacket(true,1000f), true);
                //owner.BroadcastPacket(new SCTransferTelescopeUnitsPacket(1,3,0f,0f,0f), true); // TODO Timer
            }

            DoodadManager.Instance.TriggerPhases(GetType().Name, caster, owner, FakeSkillId);
        }
    }
}

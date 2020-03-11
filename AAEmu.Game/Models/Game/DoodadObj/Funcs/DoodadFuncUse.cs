using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {

            var func = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
            
            if (func.NextPhase > 0)
            {
                owner.FuncGroupId = (uint)func.NextPhase;
                owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), false); // FIX: added to work on/off lighting and destruction of drums/boxes
                var nextfunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, 0);
                if (nextfunc != null)
                    nextfunc.Use(caster, owner, skillId);
            }

            _log.Debug("DoodadFuncUse");
        }
    }
}

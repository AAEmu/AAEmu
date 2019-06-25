using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTod : DoodadFuncTemplate
    {
        public int Tod { get; set; }
        public uint NextPhase { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncTod : NextPhase {0}, SkillId {1}", NextPhase, skillId);

            if (owner.FuncTask == null)
            {
                // выполняем действие
                owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), true);
            }
        }
    }
}

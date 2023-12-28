using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncEnterSysInstance : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint ZoneId { get; set; }
        public uint FactionId { get; set; }

        public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            Logger.Info($"DoodadFuncEnterSysInstance, ZoneId: {ZoneId}, FactionId: {FactionId}");
            if (caster is Character character)
            {
                IndunManager.Instance.RequestSysInstance(character, ZoneId);
            }
        }
    }
}

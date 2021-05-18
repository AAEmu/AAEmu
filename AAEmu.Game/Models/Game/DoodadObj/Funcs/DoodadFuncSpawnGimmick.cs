using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSpawnGimmick : DoodadFuncTemplate
    {
        public uint GimmickId { get; set; }
        public uint FactionId { get; set; }
        public float Scale { get; set; }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public float OffsetZ { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float VelocityZ { get; set; }
        public float AngleX { get; set; }
        public float AngleY { get; set; }
        public float AngleZ { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncSpawnGimmick");
            if (caster is Character)
            {
                //I think this is used to reschedule anything that needs triggered at a specific gametime
                owner.OverridePhase = NextPhase;
                owner.ToPhaseAndUse = true;
            }
            owner.ToPhaseAndUse = false;
        }
    }
}

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRatioRespawn : DoodadPhaseFuncTemplate
    {
        public int Ratio { get; set; }
        public uint SpawnDoodadId { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncRatioRespawn : Ratio {0}, SpawnDoodadId {1}", Ratio, SpawnDoodadId);

            // Doodad spawn
            if (owner.PhaseRatio <= Ratio)
            {
                var doodad = DoodadManager.Instance.Create(0, SpawnDoodadId);
                doodad.Transform = owner.Transform.Clone();
                doodad.Spawn();
                owner.Delete();

                return true; // прерываем фазовую функцию
            }

            return false;
        }
    }
}

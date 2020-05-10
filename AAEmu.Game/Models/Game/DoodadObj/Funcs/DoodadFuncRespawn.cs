using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRespawn : DoodadFuncTemplate
    {
        public int MinTime { get; set; }
        public int MaxTime { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncRespawn: MinTime {0}, MaxTime {1}", MinTime, MaxTime);

            // Doodad spawn
            if (!(caster is Character character))
            {
                return;
            }
            var doodad = new DoodadSpawner
            {
                Id = 0,
                UnitId = owner.TemplateId,
                Position = character.Position.Clone()
            };
            var (newX2, newY2) = MathUtil.AddDistanceToFront(1, doodad.Position.X, doodad.Position.Y, doodad.Position.RotationZ); //TODO distance 1 meter

            doodad.Position.X = newX2;
            doodad.Position.Y = newY2;
            doodad.Position.Z = AppConfiguration.Instance.HeightMapsEnable
                ? WorldManager.Instance.GetHeight(doodad.Position.ZoneId, doodad.Position.X, doodad.Position.Y)
                : doodad.Position.Z;

            doodad.Spawn(0);
        }
    }
}

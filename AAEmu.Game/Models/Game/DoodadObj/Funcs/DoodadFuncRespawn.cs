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
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
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
                Transform = character.Transform.Clone()
            };
            doodad.Transform.Local.AddDistanceToFront(1f);
            
            // TODO: Transform.Local is wrong for setting Z height
            doodad.Transform.Local.Position.Z = AppConfiguration.Instance.HeightMapsEnable
                ? WorldManager.Instance.GetHeight(doodad.Transform.ZoneId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y)
                : doodad.Transform.Local.Position.Z;

            doodad.Spawn(0);
        }
    }
}

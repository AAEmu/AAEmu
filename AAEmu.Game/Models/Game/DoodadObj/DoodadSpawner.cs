using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadSpawner : Spawner<Doodad>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public float Scale { get; set; }

        public override Doodad Spawn(uint objId)
        {
            var doodad = DoodadManager.Instance.Create(objId, UnitId, null); // TODO look
            doodad.Spawner = this;
            doodad.Position = Position.Clone();
            doodad.OwnerType = DoodadOwnerType.System;
            if (Scale > 0)
                doodad.SetScale(Scale);
            if (doodad.Position == null)
            {
                _log.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
                return null;
            }

            doodad.Spawn();
            return doodad;
        }

        public override void Despawn(Doodad doodad)
        {
            doodad.Delete();
        }
    }
}
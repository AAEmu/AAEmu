using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFinal : DoodadFuncTemplate
    {
        public int After { get; set; }
        public bool Respawn { get; set; }
        public int MinTime { get; set; }
        public int MaxTime { get; set; }
        public bool ShowTip { get; set; }
        public bool ShowEndTime { get; set; }
        public string Tip { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncFinal");

            if (After > 0)
            {
                owner.GrowthTime = DateTime.Now.AddMilliseconds(After); // TODO ... need here?
                owner.FuncTask = new DoodadFuncFinalTask(caster, owner, skillId, Respawn);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(After));
            }
            else
            {
                owner.Delete();
            }

            Character character = WorldManager.Instance.GetCharacterByObjId(caster.ObjId);
            if (character.Id != owner.OwnerId && owner.OwnerId != 0) //If the player is stealing something, create footprints
            {
                var doodadSpawner = new DoodadSpawner();
                doodadSpawner.Id = 0;
                switch (caster.RaceGender)
                {
                    case 17:
                        doodadSpawner.UnitId = 3313; //Male footprints
                    break;
                    case 33:
                        doodadSpawner.UnitId = 3314; //Female footprints
                    break;
                }
                doodadSpawner.Position = caster.Position.Clone();
                doodadSpawner.Position.RotationX = caster.Position.RotationX;
                doodadSpawner.Position.RotationY = caster.Position.RotationY;
                doodadSpawner.Position.RotationZ = caster.Position.RotationZ;
                var doodad = doodadSpawner.Spawn(0, 0, caster.ObjId);
            }
        }
    }
}

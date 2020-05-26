using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks.Doodads;
using AAEmu.Commons.Utils;

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
            _log.Debug("DoodadFuncFinal: skillId {0}, After {1}, Respawn {2}, MinTime {3}, MaxTime {4}, ShowTip {5}, ShowEndTime {6}, Tip {7}",
                skillId, After, Respawn, MinTime, MaxTime, ShowTip, ShowEndTime, Tip);

            var delay = Rand.Next(MinTime, MaxTime);
            Character character = WorldManager.Instance.GetCharacterByObjId(caster.ObjId);
            if (character != null)
            {
                const int count = 1;
                var itemTemplate = ItemManager.Instance.GetItemIdsFromDoodad(owner.TemplateId);
                if (itemTemplate != null)
                {
                    foreach (var itemId in itemTemplate)
                    {
                        if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.AutoLootDoodadItem, itemId, count))
                        {
                            // TODO: do proper handling of insufficient bag space
                            character.SendErrorMessage(Error.ErrorMessageType.BagFull);
                        }
                    }
                }
            }
            if (After > 0)
            {
                owner.GrowthTime = DateTime.Now.AddMilliseconds(delay); // TODO ... need here?
                owner.FuncTask = new DoodadFuncFinalTask(caster, owner, skillId, Respawn);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(After)); // After ms remove the object from visibility
            }
            else
            {
                owner.Delete();
            }

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
                _ = doodadSpawner.Spawn(0, 0, caster.ObjId);
            }
        }
    }
}

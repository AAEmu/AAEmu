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
    public class DoodadFuncFinal : DoodadPhaseFuncTemplate
    {
        public int After { get; set; }
        public bool Respawn { get; set; }
        public int MinTime { get; set; }
        public int MaxTime { get; set; }
        public bool ShowTip { get; set; }
        public bool ShowEndTime { get; set; }
        public string Tip { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Debug("DoodadFuncFinal: After {0}, Respawn {1}, MinTime {2}, MaxTime {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}",
                After, Respawn, MinTime, MaxTime, ShowTip, ShowEndTime, Tip);

            var delay = Rand.Next(MinTime, MaxTime);

            // if (caster is Character character)
            // {
            //     const int count = 1;
            //     var itemTemplate = ItemManager.Instance.GetItemIdsFromDoodad(owner.TemplateId);
            //     if (itemTemplate != null)
            //     {
            //         foreach (var itemId in itemTemplate)
            //         {
            //             if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.AutoLootDoodadItem, itemId, count))
            //             {
            //                 // TODO: do proper handling of insufficient bag space
            //                 character.SendErrorMessage(Error.ErrorMessageType.BagFull);
            //             }
            //         }
            //     }
            // }
            if (After > 0)
            {
                //if (owner.FuncTask != null)
                //{
                //    _ = owner.FuncTask.Cancel();
                //    _ = owner.FuncTask = null;
                //    _log.Debug("DoodadFuncFinal: The current timer has been canceled by the next scheduled timer.");
                //}
                owner.FuncTask = new DoodadFuncFinalTask(caster, owner, 0, Respawn, delay);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(After)); // After ms remove the object from visibility
            }
            else
            {
                owner.Delete();
            }

            return false;
        }
    }
}

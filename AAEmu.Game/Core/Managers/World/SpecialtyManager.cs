using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class SpecialtyManager : Singleton<SpecialtyManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, Specialty> _specialties;
        private Dictionary<uint, SpecialtyBundleItem> _specialtyBundleItems;
        private Dictionary<uint, SpecialtyNpc> _specialtyNpc;

        public Specialty GetSpecialty(uint specialtyId)
        {
            return _specialties.ContainsKey(specialtyId) ? _specialties[specialtyId] : null;
        }
        
        public void Load()
        {
            _specialties = new Dictionary<uint, Specialty>();
            _specialtyBundleItems = new Dictionary<uint, SpecialtyBundleItem>();
            _specialtyNpc = new Dictionary<uint, SpecialtyNpc>();
            
            _log.Info("SpecialtyManager is loading...");

            ItemManager.Instance.OnItemsLoaded += OnItemsLoaded;

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM specialties";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new Specialty();
                            template.Id = reader.GetUInt32("id");
                            template.RowZoneGroupId = reader.GetUInt32("row_zone_group_id");
                            template.ColZoneGroupId = reader.GetUInt32("col_zone_group_id");
                            template.Ratio = reader.GetUInt32("ratio");
                            template.Profit = reader.GetUInt32("profit");
                            template.VendorExist = reader.GetBoolean("id", true);
                            _specialties.Add(template.Id, template);
                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM specialty_bundle_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SpecialtyBundleItem();
                            template.Id = reader.GetUInt32("id");
                            template.ItemId = reader.GetUInt32("item_id");
                            template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");
                            template.Profit = reader.GetUInt32("profit");
                            template.Ratio = reader.GetUInt32("ratio");
                            _specialtyBundleItems.Add(template.Id, template);
                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM specialty_npcs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SpecialtyNpc();
                            template.Id = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.NpcId = reader.GetUInt32("npc_id");
                            template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");
                            
                            _specialtyNpc.Add(template.NpcId, template);
                        }
                    }
                }
            }
            
            _log.Info("SpecialtyManager loaded");
        }

        public void OnItemsLoaded(object sender, EventArgs e)
        {
            foreach (var specialtyBundleItem in _specialtyBundleItems.Values)
            {
                specialtyBundleItem.Item = ItemManager.Instance.GetTemplate(specialtyBundleItem.ItemId);
            }
        }

        public uint GetRatioForSpecialty(Unit player, uint specialtyId)
        {
            return 130;
        }

        public int GetBasePriceForSpecialty(Character player, uint npcId)
        {
            // Sanity checks
            var backpack = player.Equip[(int) EquipmentItemSlot.Backpack];
            if (backpack == null) 
                return 0; // Player is fucking with us here
            
            var npc = WorldManager.Instance.GetNpc(npcId);
            if (npc == null) return 0;

            if (MathUtil.CalculateDistance(player.Position, npc.Position) > 2.5)
                return 0; // Player is too far from the NPC -> fucking with us

            var bundleIdAtNPC = _specialtyNpc[npc.TemplateId].SpecialtyBundleId;

            var bundleItem = _specialtyBundleItems.Values.First(p => p.ItemId == backpack.TemplateId && p.SpecialtyBundleId == bundleIdAtNPC);
            if (bundleItem == null) return 0;
            
            return (int) ((bundleItem.Profit * (bundleItem.Ratio / 1000)) + bundleItem.Item.Refund);
        }
    }
}

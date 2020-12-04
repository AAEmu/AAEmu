using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class DeclareDominion: SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int unused1,
            int unused2,
            int unused3,
            int unused4)
        {
            if (caster.Expedition == null)
                return;
            
            // Check target is not already claimed
            if (!(target is House lodestone))
                return;
            
            // Get target zone, radius, etc..
            
            // Advance building step on target
            
            // Create new dominion data
            var dominion = new DominionData()
            {
                House = lodestone.Id,
                X = lodestone.Position.X,
                Y = lodestone.Position.Y,
                Z = lodestone.Position.Z,
                TaxRate = 50,
                ReignStartTime = DateTime.Now,
                ExpeditionId = caster.Expedition.Id,
                CurHouseTaxMoney = 500000,
                CurHuntTaxMoney = 9000,
                PeaceTaxMoney = 300000,
                CurHouseTaxAaPoint = 0,
                PeaceTaxAaPoint = 0,
                LastPaidTime = DateTime.Now,
                LastSiegeEndTime = DateTime.Now,
                LastTaxRateChangedTime = DateTime.Now,
                LastNationalTaxRateChagedTime = DateTime.Now,
                NationalTaxRate = 500,
                NationalMonumentDbId = 0,
                NationalMonumentX = 0,
                NationalMonumentY = 0,
                NationalMonumentZ = 0,
                TerritoryData = new DominionTerritoryData()
                {
                  Id = 6,
                  Id2 = 4771,
                  MaxGates = 1,
                  MaxWalls = 50,
                  RadiusDeclare = 250,
                  RadiusDominion = 110,
                  RadiusSiege = 250,
                  RadiusOffenseHq = 100
                },
                SiegeTimers = new DominionSiegeTimers()
                {
                    Bdm = 0,
                    Durations = new int[]{0,0,0,0,0},
                    Fixed = DateTime.MinValue,
                    Started = DateTime.MinValue,
                    SiegePeriod = 1,
                    UnkData = new DominionUnkData()
                    {
                        Id = 0,
                        Limit = 0,
                        Ni = 0,
                        Nr = 0,
                        X = 0,
                        Y = 0,
                        Z = 0,
                        ObjId = 4,
                        UnkIds = new uint[]{}
                    },
                    Unk2Data = new DominionUnkData()
                    {
                        Id = 0,
                        Limit = 0,
                        Ni = 0,
                        Nr = 0,
                        X = 0,
                        Y = 0,
                        Z = 0,
                        ObjId = 0,
                        UnkIds = new uint[]{}
                    }
                },
                NonPvPDuration = 0,
                NonPvPStart = DateTime.Now,
                ZoneId = (ushort) ZoneManager.Instance.GetZoneByKey(lodestone.Position.ZoneId).GroupId,
                ObjId = 0
            };
            
            // Broadcast packet to the entire server
            WorldManager.Instance.BroadcastPacketToServer(new SCDominionDataPacket(dominion, true, true));
            if (caster is Character character)
            {
                // character.Inventory.Equipment.
                var backpack = character.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
                character.Inventory.Equipment.ConsumeItem(ItemTaskType.SkillReagents, backpack.TemplateId, 1, backpack);
            }
        }
    }
}

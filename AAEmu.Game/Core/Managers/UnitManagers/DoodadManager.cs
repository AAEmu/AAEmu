﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Database.Mysql;
using AAEmu.Game.Core.Database.Sqllite;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;

using NLog;
using Character = AAEmu.Game.Models.Game.Char.Character;
using Doodad = AAEmu.Game.Models.Game.DoodadObj.Doodad;
using DoodadFunc = AAEmu.Game.Models.Game.DoodadObj.DoodadFunc;
using DoodadFuncAnimate = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncAnimate;
using DoodadFuncAreaTrigger = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncAreaTrigger;
using DoodadFuncAttachment = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncAttachment;
using DoodadFuncBinding = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncBinding;
using DoodadFuncBubble = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncBubble;
using DoodadFuncBuff = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncBuff;
using DoodadFuncButcher = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncButcher;
using DoodadFuncBuyFish = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncBuyFish;
using DoodadFuncBuyFishItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncBuyFishItem;
using DoodadFuncBuyFishModel = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncBuyFishModel;
using DoodadFuncCatch = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCatch;
using DoodadFuncCerealHarvest = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCerealHarvest;
using DoodadFuncCleanupLogicLink = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCleanupLogicLink;
using DoodadFuncClimateReact = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncClimateReact;
using DoodadFuncClimb = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncClimb;
using DoodadFuncClout = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncClout;
using DoodadFuncCoffer = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCoffer;
using DoodadFuncCofferPerm = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCofferPerm;
using DoodadFuncConditionalUse = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConditionalUse;
using DoodadFuncConsumeChanger = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConsumeChanger;
using DoodadFuncConsumeChangerItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConsumeChangerItem;
using DoodadFuncConsumeChangerModel = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConsumeChangerModel;
using DoodadFuncConsumeChangerModelItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConsumeChangerModelItem;
using DoodadFuncConsumeItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConsumeItem;
using DoodadFuncConvertFish = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConvertFish;
using DoodadFuncConvertFishItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncConvertFishItem;
using DoodadFuncCraftAct = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftAct;
using DoodadFuncCraftCancel = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftCancel;
using DoodadFuncCraftDirect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftDirect;
using DoodadFuncCraftGetItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftGetItem;
using DoodadFuncCraftInfo = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftInfo;
using DoodadFuncCraftPack = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftPack;
using DoodadFuncCraftStart = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftStart;
using DoodadFuncCraftStartCraft = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCraftStartCraft;
using DoodadFuncCropHarvest = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCropHarvest;
using DoodadFuncCrystalCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCrystalCollect;
using DoodadFuncCutdown = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCutdown;
using DoodadFuncCutdowning = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncCutdowning;
using DoodadFuncDairyCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncDairyCollect;
using DoodadFuncDeclareSiege = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncDeclareSiege;
using DoodadFuncDig = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncDig;
using DoodadFuncDigTerrain = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncDigTerrain;
using DoodadFuncDyeingredientCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncDyeingredientCollect;
using DoodadFuncEnterInstance = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncEnterInstance;
using DoodadFuncEnterSysInstance = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncEnterSysInstance;
using DoodadFuncEvidenceItemLoot = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncEvidenceItemLoot;
using DoodadFuncExchange = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncExchange;
using DoodadFuncExitIndun = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncExitIndun;
using DoodadFuncFakeUse = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncFakeUse;
using DoodadFuncFeed = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncFeed;
using DoodadFuncFiberCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncFiberCollect;
using DoodadFuncFinal = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncFinal;
using DoodadFuncFishSchool = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncFishSchool;
using DoodadFuncFruitPick = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncFruitPick;
using DoodadFuncGassExtract = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncGassExtract;
using DoodadFuncGrowth = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncGrowth;
using DoodadFuncHarvest = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncHarvest;
using DoodadFuncHouseFarm = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncHouseFarm;
using DoodadFuncHousingArea = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncHousingArea;
using DoodadFuncHunger = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncHunger;
using DoodadFuncInsertCounter = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncInsertCounter;
using DoodadFuncLogic = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncLogic;
using DoodadFuncLogicFamilyProvider = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncLogicFamilyProvider;
using DoodadFuncLogicFamilySubscriber = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncLogicFamilySubscriber;
using DoodadFuncLootItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncLootItem;
using DoodadFuncLootPack = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncLootPack;
using DoodadFuncMachinePartsCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncMachinePartsCollect;
using DoodadFuncMedicalingredientMine = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncMedicalingredientMine;
using DoodadFuncMow = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncMow;
using DoodadFuncNaviMarkPosToMap = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviMarkPosToMap;
using DoodadFuncNaviNaming = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviNaming;
using DoodadFuncNaviOpenBounty = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviOpenBounty;
using DoodadFuncNaviOpenMailbox = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviOpenMailbox;
using DoodadFuncNaviOpenPortal = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviOpenPortal;
using DoodadFuncNaviRemove = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviRemove;
using DoodadFuncNaviRemoveTimer = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviRemoveTimer;
using DoodadFuncNaviTeleport = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncNaviTeleport;
using DoodadFuncOpenFarmInfo = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncOpenFarmInfo;
using DoodadFuncOpenPaper = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncOpenPaper;
using DoodadFuncOreMine = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncOreMine;
using DoodadFuncParentInfo = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncParentInfo;
using DoodadFuncParrot = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncParrot;
using DoodadFuncPlantCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPlantCollect;
using DoodadFuncPlayFlowGraph = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPlayFlowGraph;
using DoodadFuncPulse = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPulse;
using DoodadFuncPulseTrigger = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPulseTrigger;
using DoodadFuncPurchase = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPurchase;
using DoodadFuncPuzzleIn = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPuzzleIn;
using DoodadFuncPuzzleOut = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPuzzleOut;
using DoodadFuncPuzzleRoll = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncPuzzleRoll;
using DoodadFuncQuest = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncQuest;
using DoodadFuncRatioChange = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRatioChange;
using DoodadFuncRatioRespawn = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRatioRespawn;
using DoodadFuncRecoverItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRecoverItem;
using DoodadFuncRemoveInstance = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRemoveInstance;
using DoodadFuncRemoveItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRemoveItem;
using DoodadFuncRenewItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRenewItem;
using DoodadFuncRequireItem = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRequireItem;
using DoodadFuncRequireQuest = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRequireQuest;
using DoodadFuncRespawn = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRespawn;
using DoodadFuncRockMine = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncRockMine;
using DoodadFuncSeedCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSeedCollect;
using DoodadFuncShear = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncShear;
using DoodadFuncSiegePeriod = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSiegePeriod;
using DoodadFuncSign = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSign;
using DoodadFuncSkillHit = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSkillHit;
using DoodadFuncSkinOff = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSkinOff;
using DoodadFuncSoilCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSoilCollect;
using DoodadFuncSpawnGimmick = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSpawnGimmick;
using DoodadFuncSpawnMgmt = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSpawnMgmt;
using DoodadFuncSpiceCollect = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncSpiceCollect;
using DoodadFuncStampMaker = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncStampMaker;
using DoodadFuncStoreUi = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncStoreUi;
using DoodadFuncTimer = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncTimer;
using DoodadFuncTod = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncTod;
using DoodadFuncUccImprint = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncUccImprint;
using DoodadFuncUse = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncUse;
using DoodadFuncWaterVolume = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncWaterVolume;
using DoodadFuncZoneReact = AAEmu.Game.Models.Game.DoodadObj.Funcs.DoodadFuncZoneReact;
using DoodadPhaseFunc = AAEmu.Game.Models.Game.DoodadObj.DoodadPhaseFunc;
using Transfer = AAEmu.Game.Models.Game.Units.Transfer;

namespace AAEmu.Game.Core.Managers.UnitManagers
{
    public class DoodadManager : Singleton<DoodadManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _loaded = false;

        private Dictionary<uint, DoodadTemplate> _templates;
        private Dictionary<uint, DoodadFuncGroups> _allFuncGroups;
        private Dictionary<uint, List<DoodadFunc>> _funcsByGroups;
        private Dictionary<uint, DoodadFunc> _funcsById;
        private Dictionary<uint, List<DoodadPhaseFunc>> _phaseFuncs;
        private Dictionary<string, Dictionary<uint, DoodadFuncTemplate>> _funcTemplates;
        private Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>> _phaseFuncTemplates;

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        public DoodadTemplate GetTemplate(uint id)
        {
            return Exist(id) ? _templates[id] : null;
        }

        public void Load()
        {
            if (_loaded)
                return;
            
            var _sqlliteContext = new AAEmuSqlliteDbContext();
            var _mysqlContext = new AAEmuMysqlDbContext();

            var doodadAlmighties = _sqlliteContext.DoodadAlmighties.ToList();
            var doodads = _mysqlContext.Doodads.ToList();
            

            _templates = new Dictionary<uint, DoodadTemplate>();
            _allFuncGroups = new Dictionary<uint, DoodadFuncGroups>();
            _funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
            _funcsById = new Dictionary<uint, DoodadFunc>();
            _phaseFuncs = new Dictionary<uint, List<DoodadPhaseFunc>>();
            _funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
            _phaseFuncTemplates = new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>>();
            foreach (var type in Helpers.GetTypesInNamespace(Assembly.GetAssembly(GetType()), "AAEmu.Game.Models.Game.DoodadObj.Funcs"))
                if (type.BaseType == typeof(DoodadFuncTemplate))
                    _funcTemplates.Add(type.Name, new Dictionary<uint, DoodadFuncTemplate>());
                else if (type.BaseType == typeof(DoodadPhaseFuncTemplate))
                    _phaseFuncTemplates.Add(type.Name, new Dictionary<uint, DoodadPhaseFuncTemplate>());

            using (var connection = SQLite.CreateConnection())
            {
                #region doodad_funcs
                _log.Info("Loading doodad functions ...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_groups ORDER BY doodad_almighty_id ASC, doodad_func_group_kind_id ASC";
                    command.Prepare();
                    using (var sqliteDataReaderChild = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReaderChild))
                    {
                        while (reader.Read())
                        {
                            var funcGroups = new DoodadFuncGroups();
                            funcGroups.Id = reader.GetUInt32("id");
                            funcGroups.Almighty = reader.GetUInt32("doodad_almighty_id");
                            funcGroups.GroupKindId = (DoodadFuncGroups.DoodadFuncGroupKind)reader.GetUInt32("doodad_func_group_kind_id");
                            funcGroups.SoundId = reader.GetUInt32("sound_id", 0);
                            funcGroups.Model = reader.GetString("model", "");

                            var template = GetTemplate(funcGroups.Almighty);
                            if (template != null)
                                template.FuncGroups.Add(funcGroups);
                        }
                    }
                }

                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_funcs ORDER BY doodad_func_group_id ASC, actual_func_id ASC";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFunc();
                            func.FuncKey = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("doodad_func_group_id");
                            func.FuncId = reader.GetUInt32("actual_func_id");
                            func.FuncType = reader.GetString("actual_func_type");
                            func.NextPhase = reader.GetInt32("next_phase", -1); // TODO next_phase = 0?
                            func.SoundId = reader.IsDBNull("sound_id") ? 0 : reader.GetUInt32("sound_id");
                            func.SkillId = reader.GetUInt32("func_skill_id", 0);
                            func.PermId = reader.GetUInt32("perm_id");
                            func.Count = reader.GetInt32("act_count", 0);
                            List<DoodadFunc> tempListGroups;
                            if (_funcsByGroups.ContainsKey(func.GroupId))
                                tempListGroups = _funcsByGroups[func.GroupId];
                            else
                            {
                                tempListGroups = new List<DoodadFunc>();
                                _funcsByGroups.Add(func.GroupId, tempListGroups);
                            }
                            tempListGroups.Add(func);
                            _funcsById.Add(func.FuncKey, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_phase_funcs ORDER BY doodad_func_group_id ASC, actual_func_id ASC";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                           var func = new DoodadPhaseFunc();
                            func.GroupId = reader.GetUInt32("doodad_func_group_id");
                            func.FuncId = reader.GetUInt32("actual_func_id");
                            func.FuncType = reader.GetString("actual_func_type");
                            List<DoodadPhaseFunc> list;
                            if (_phaseFuncs.ContainsKey(func.GroupId))
                                list = _phaseFuncs[func.GroupId];
                            else
                            {
                                list = new List<DoodadPhaseFunc>();
                                _phaseFuncs.Add(func.GroupId, list);
                            }

                            list.Add(func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_animates";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncAnimate();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            func.PlayOnce = reader.GetBoolean("play_once", true);
                            _phaseFuncTemplates["DoodadFuncAnimate"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_area_triggers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncAreaTrigger();
                            func.Id = reader.GetUInt32("id");
                            func.NpcId = reader.GetUInt32("npc_id", 0);
                            func.IsEnter = reader.GetBoolean("is_enter", true);
                            _funcTemplates["DoodadFuncAreaTrigger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_attachments";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncAttachment();
                            func.Id = reader.GetUInt32("id");
                            func.AttachPointId = (AttachPointKind)reader.GetByte("attach_point_id");
                            func.Space = reader.GetInt32("space");
                            func.BondKindId = (BondKind)reader.GetByte("bond_kind_id");
                            _funcTemplates["DoodadFuncAttachment"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_bindings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBinding();
                            func.Id = reader.GetUInt32("id");
                            func.DistrictId = reader.GetUInt32("district_id");
                            _funcTemplates["DoodadFuncBinding"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_bubbles";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBubble();
                            func.Id = reader.GetUInt32("id");
                            func.BubbleId = reader.GetUInt32("bubble_id");
                            _funcTemplates["DoodadFuncBubble"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buffs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuff();
                            func.Id = reader.GetUInt32("id");
                            func.BuffId = reader.GetUInt32("buff_id");
                            func.Radius = reader.GetFloat("radius");
                            func.Count = reader.GetInt32("count");
                            func.PermId = reader.GetUInt32("perm_id");
                            func.RelationshipId = reader.GetUInt32("relationship_id");
                            _funcTemplates["DoodadFuncBuff"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_butchers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncButcher();
                            func.Id = reader.GetUInt32("id");
                            func.CorpseModel = reader.GetString("corpse_model");
                            _funcTemplates["DoodadFuncButcher"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buy_fish_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuyFishItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncBuyFishId = reader.GetUInt32("doodad_func_buy_fish_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _phaseFuncTemplates["DoodadFuncBuyFishItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buy_fish_models";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuyFishModel();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            _phaseFuncTemplates["DoodadFuncBuyFishModel"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buy_fishes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuyFish();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            _funcTemplates["DoodadFuncBuyFish"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_catches";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCatch();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCatch"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cereal_harvests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCerealHarvest();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCerealHarvest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cleanup_logic_links";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCleanupLogicLink();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCleanupLogicLink"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_climate_reacts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncClimateReact();
                            func.Id = reader.GetUInt32("id");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            _phaseFuncTemplates["DoodadFuncClimateReact"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_climbs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncClimb();
                            func.Id = reader.GetUInt32("id");
                            func.ClimbTypeId = reader.GetUInt32("climb_type_id");
                            func.AllowHorizontalMultiHanger = reader.GetBoolean("allow_horizontal_multi_hanger", true);
                            _funcTemplates["DoodadFuncClimb"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_clouts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncClout();
                            func.Id = reader.GetUInt32("id");
                            func.Duration = reader.GetInt32("duration");
                            func.Tick = reader.GetInt32("tick");
                            func.TargetRelation = (SkillTargetRelation)reader.GetUInt32("target_relation_id");
                            func.BuffId = reader.GetUInt32("buff_id", 0);
                            func.ProjectileId = reader.GetUInt32("projectile_id", 0);
                            func.ShowToFriendlyOnly = reader.GetBoolean("show_to_friendly_only", true);
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            func.AoeShapeId = reader.GetUInt32("aoe_shape_id");
                            func.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                            func.TargetNoBuffTagId = reader.GetUInt32("target_no_buff_tag_id", 0);
                            func.UseOriginSource = reader.GetBoolean("use_origin_source", true);
                            func.Effects = new List<uint>();
                            _phaseFuncTemplates["DoodadFuncClout"].Add(func.Id, func);
                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_clout_effects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var funcCloutId = reader.GetUInt32("doodad_func_clout_id");
                            var func = (DoodadFuncClout)_phaseFuncTemplates["DoodadFuncClout"][funcCloutId];
                            func.Effects.Add(reader.GetUInt32("effect_id"));
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_coffer_perms";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCofferPerm();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCofferPerm"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_coffers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCoffer();
                            func.Id = reader.GetUInt32("id");
                            func.Capacity = reader.GetInt32("capacity");
                            _phaseFuncTemplates["DoodadFuncCoffer"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_conditional_uses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConditionalUse();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id", 0);
                            func.FakeSkillId = reader.GetUInt32("fake_skill_id", 0);
                            func.QuestId = reader.GetUInt32("quest_id", 0);
                            func.QuestTriggerPhase = reader.GetUInt32("quest_trigger_phase", 0);
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            func.ItemTriggerPhase = reader.GetUInt32("item_trigger_phase", 0);
                            _funcTemplates["DoodadFuncConditionalUse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changer_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChangerItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncConsumeChangerId = reader.GetUInt32("doodad_func_consume_changer_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _phaseFuncTemplates["DoodadFuncConsumeChangerItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changer_model_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChangerModelItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncConsumeChangerModelId = reader.GetUInt32("doodad_func_consume_changer_model_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _phaseFuncTemplates["DoodadFuncConsumeChangerModelItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changer_models";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChangerModel();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            _phaseFuncTemplates["DoodadFuncConsumeChangerModel"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChanger();
                            func.Id = reader.GetUInt32("id");
                            func.SlotId = reader.GetUInt32("slot_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncConsumeChanger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeItem();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _phaseFuncTemplates["DoodadFuncConsumeItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_convert_fish_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConvertFishItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncConvertFishId = reader.GetUInt32("doodad_func_convert_fish_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.LootPackId = reader.GetUInt32("loot_pack_id");
                            _phaseFuncTemplates["DoodadFuncConvertFishItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_convert_fishes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConvertFish();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncConvertFish"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_acts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftAct();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftAct"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_cancels";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftCancel();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftCancel"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_directs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftDirect();
                            func.Id = reader.GetUInt32("id");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            _phaseFuncTemplates["DoodadFuncCraftDirect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_get_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftGetItem();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftGetItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_infos";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftInfo();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftInfo"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_packs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftPack();
                            func.Id = reader.GetUInt32("id");
                            func.CraftPackId = reader.GetUInt32("craft_pack_id");
                            _funcTemplates["DoodadFuncCraftPack"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_start_crafts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftStartCraft();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncCraftStartId = reader.GetUInt32("doodad_func_craft_start_id");
                            func.CraftId = reader.GetUInt32("craft_id");
                            _phaseFuncTemplates["DoodadFuncCraftStartCraft"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_starts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftStart();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftStart"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_crop_harvests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCropHarvest();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCropHarvest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_crystal_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCrystalCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCrystalCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cutdownings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCutdowning();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCutdowning"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cutdowns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCutdown();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCutdown"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_dairy_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDairyCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDairyCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_declare_sieges";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDeclareSiege();
                            func.Id = reader.GetUInt32("id");
                            _phaseFuncTemplates["DoodadFuncDeclareSiege"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_digs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDig();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDig"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_dig_terrains";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDigTerrain();
                            func.Id = reader.GetUInt32("id");
                            func.Radius = reader.GetInt32("radius");
                            func.Life = reader.GetInt32("life");
                            _funcTemplates["DoodadFuncDigTerrain"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_dyeingredient_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDyeingredientCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDyeingredientCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_enter_instances";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncEnterInstance();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneId = reader.GetUInt32("zone_id");
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            _funcTemplates["DoodadFuncEnterInstance"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_enter_sys_instances";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncEnterSysInstance();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneId = reader.GetUInt32("zone_id");
                            func.FactionId = reader.GetUInt32("faction_id", 0);
                            _funcTemplates["DoodadFuncEnterSysInstance"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_evidence_item_loots";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncEvidenceItemLoot();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id");
                            func.CrimeValue = reader.GetInt32("crime_value");
                            func.CrimeKindId = reader.GetUInt32("crime_kind_id");
                            _funcTemplates["DoodadFuncEvidenceItemLoot"].Add(func.Id, func);
                        }
                    }
                }

                // TODO doodad_func_exchange_items( id INT, doodad_func_exchange_id INT, item_id INT, loot_pack_id INT )

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_exchanges";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncExchange();
                            func.Id = reader.GetUInt32("id");
                            _phaseFuncTemplates["DoodadFuncExchange"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_exit_induns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncExitIndun();
                            func.Id = reader.GetUInt32("id");
                            func.ReturnPointId = reader.GetUInt32("return_point_id", 0);
                            _funcTemplates["DoodadFuncExitIndun"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fake_uses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFakeUse();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id", 0);
                            func.FakeSkillId = reader.GetUInt32("fake_skill_id", 0);
                            func.TargetParent = reader.GetBoolean("target_parent", true);
                            _funcTemplates["DoodadFuncFakeUse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_feeds";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFeed();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncFeed"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fiber_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFiberCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncFiberCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_finals";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFinal();
                            func.Id = reader.GetUInt32("id");
                            func.After = reader.GetInt32("after", 0);
                            func.Respawn = reader.GetBoolean("respawn", true);
                            func.MinTime = reader.GetInt32("min_time", 0);
                            func.MaxTime = reader.GetInt32("max_time", 0);
                            func.ShowTip = reader.GetBoolean("show_tip", true);
                            func.ShowEndTime = reader.GetBoolean("show_end_time", true);
                            func.Tip = reader.GetString("tip");
                            _phaseFuncTemplates["DoodadFuncFinal"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fish_schools";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFishSchool();
                            func.Id = reader.GetUInt32("id");
                            func.NpcSpawnerId = reader.GetUInt32("npc_spawner_id");
                            _phaseFuncTemplates["DoodadFuncFishSchool"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fruit_picks";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFruitPick();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncFruitPick"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_gass_extracts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncGassExtract();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncGassExtract"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_growths";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncGrowth();
                            func.Id = reader.GetUInt32("id");
                            func.Delay = reader.GetInt32("delay");
                            func.StartScale = reader.GetInt32("start_scale");
                            func.EndScale = reader.GetInt32("end_scale");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            _phaseFuncTemplates["DoodadFuncGrowth"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_harvests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHarvest();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncHarvest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_house_farms";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHouseFarm();
                            func.Id = reader.GetUInt32("id");
                            func.ItemCategoryId = reader.GetUInt32("item_category_id");
                            _phaseFuncTemplates["DoodadFuncHouseFarm"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_housing_areas";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHousingArea();
                            func.Id = reader.GetUInt32("id");
                            func.FactionId = reader.GetUInt32("faction_id");
                            func.Radius = reader.GetInt32("radius");
                            _funcTemplates["DoodadFuncHousingArea"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_hungers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHunger();
                            func.Id = (uint)reader.GetInt32("id");
                            func.HungryTerm = reader.GetInt32("hungry_term");
                            func.FullStep = reader.GetInt32("full_step");
                            func.PhaseChangeLimit = reader.GetInt32("phase_change_limit");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1;
                            _phaseFuncTemplates["DoodadFuncHunger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_insert_counters";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncInsertCounter();
                            func.Id = reader.GetUInt32("id");
                            func.Count = reader.GetInt32("count");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.ItemCount = reader.GetInt32("item_count");
                            _funcTemplates["DoodadFuncInsertCounter"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_logics";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLogic();
                            func.Id = reader.GetUInt32("id");
                            func.OperationId = reader.GetUInt32("operation_id");
                            func.DelayId = reader.GetUInt32("delay_id");
                            _phaseFuncTemplates["DoodadFuncLogic"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_logic_family_providers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLogicFamilyProvider();
                            func.Id = reader.GetUInt32("id");
                            func.FamilyId = reader.GetUInt32("family_id");
                            _phaseFuncTemplates["DoodadFuncLogicFamilyProvider"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_logic_family_subscribers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLogicFamilySubscriber();
                            func.Id = reader.GetUInt32("id");
                            func.FamilyId = reader.GetUInt32("family_id");
                            _phaseFuncTemplates["DoodadFuncLogicFamilySubscriber"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_loot_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLootItem();
                            func.Id = reader.GetUInt32("id");
                            func.WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.CountMin = reader.GetInt32("count_min");
                            func.CountMax = reader.GetInt32("count_max");
                            func.Percent = reader.GetInt32("percent");
                            func.RemainTime = reader.GetInt32("remain_time");
                            func.GroupId = reader.GetUInt32("group_id");
                            _funcTemplates["DoodadFuncLootItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_loot_packs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLootPack();
                            func.Id = reader.GetUInt32("id");
                            func.LootPackId = reader.GetUInt32("loot_pack_id");
                            _funcTemplates["DoodadFuncLootPack"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_machine_parts_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncMachinePartsCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncMachinePartsCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_medicalingredient_mines";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncMedicalingredientMine();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncMedicalingredientMine"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_mows";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncMow();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncMow"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_mark_pos_to_maps";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviMarkPosToMap();
                            func.Id = reader.GetUInt32("id");
                            func.X = reader.GetInt32("x");
                            func.Y = reader.GetInt32("y");
                            func.Z = reader.GetInt32("z");
                            _funcTemplates["DoodadFuncNaviMarkPosToMap"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_namings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviNaming();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviNaming"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_open_bounties";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviOpenBounty();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviOpenBounty"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_open_mailboxes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviOpenMailbox();
                            func.Id = reader.GetUInt32("id");
                            func.Duration = reader.GetInt32("duration");
                            _funcTemplates["DoodadFuncNaviOpenMailbox"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_open_portals";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviOpenPortal();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviOpenPortal"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_remove_timers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviRemoveTimer();
                            func.Id = reader.GetUInt32("id");
                            func.After = reader.GetInt32("after");
                            _phaseFuncTemplates["DoodadFuncNaviRemoveTimer"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_removes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviRemove();
                            func.Id = reader.GetUInt32("id");
                            func.ReqLaborPower = reader.GetInt32("req_lp");
                            _funcTemplates["DoodadFuncNaviRemove"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_teleports";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviTeleport();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviTeleport"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_open_farm_infos";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncOpenFarmInfo();
                            func.Id = reader.GetUInt32("id");
                            func.FarmId = reader.GetUInt32("farm_id");
                            _funcTemplates["DoodadFuncOpenFarmInfo"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_open_papers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncOpenPaper();
                            func.Id = reader.GetUInt32("id");
                            func.BookPageId = reader.GetUInt32("book_page_id", 0);
                            func.BookId = reader.GetUInt32("book_id", 0);
                            _funcTemplates["DoodadFuncOpenPaper"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ore_mines";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncOreMine();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncOreMine"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_parent_infos";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncParentInfo();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncParentInfo"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_parrots";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncParrot();
                            func.Id = reader.GetUInt32("id");
                            _phaseFuncTemplates["DoodadFuncParrot"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_plant_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPlantCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncPlantCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_play_flow_graphs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPlayFlowGraph();
                            func.Id = reader.GetUInt32("id");
                            func.EventOnPhaseChangeId = reader.GetUInt32("event_on_phase_change_id");
                            func.EventOnVisibleId = reader.GetUInt32("event_on_visible_id");
                            _phaseFuncTemplates["DoodadFuncPlayFlowGraph"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_pulse_triggers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPulseTrigger();
                            func.Id = reader.GetUInt32("id");
                            func.Flag = reader.GetBoolean("flag", true);
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1;
                            _phaseFuncTemplates["DoodadFuncPulseTrigger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_pulses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPulse();
                            func.Id = reader.GetUInt32("id");
                            func.Flag = reader.GetBoolean("flag", true);
                            _phaseFuncTemplates["DoodadFuncPulse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_purchases";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPurchase();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            func.Count = reader.GetInt32("count");
                            func.CoinItemId = reader.GetUInt32("coin_item_id", 0);
                            func.CoinCount = reader.GetInt32("coin_count", 0);
                            func.CurrencyId = reader.GetUInt32("currency_id");
                            _funcTemplates["DoodadFuncPurchase"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_puzzle_ins";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPuzzleIn();
                            func.Id = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("group_id");
                            func.Ratio = reader.GetFloat("ratio");
                            _phaseFuncTemplates["DoodadFuncPuzzleIn"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_puzzle_outs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPuzzleOut();
                            func.Id = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("group_id");
                            func.Ratio = reader.GetFloat("ratio");
                            func.Anim = reader.GetString("anim");
                            func.ProjectileId = reader.GetUInt32("projectile_id", 0);
                            func.ProjectileDelay = reader.GetInt32("projectile_delay");
                            func.LootPackId = reader.GetUInt32("loot_pack_id", 0);
                            func.Delay = reader.GetInt32("delay");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            _phaseFuncTemplates["DoodadFuncPuzzleOut"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_puzzle_rolls";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPuzzleRoll();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncPuzzleRoll"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_quests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncQuest();
                            func.Id = reader.GetUInt32("id");
                            func.QuestKindId = reader.GetUInt32("quest_kind_id");
                            func.QuestId = reader.GetUInt32("quest_id");
                            _funcTemplates["DoodadFuncQuest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ratio_changes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRatioChange();
                            func.Id = reader.GetUInt32("id");
                            func.Ratio = reader.GetInt32("ratio");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1;
                            _phaseFuncTemplates["DoodadFuncRatioChange"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ratio_respawns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRatioRespawn();
                            func.Id = reader.GetUInt32("id");
                            func.Ratio = reader.GetInt32("ratio");
                            func.SpawnDoodadId = reader.GetUInt32("spawn_doodad_id");
                            _phaseFuncTemplates["DoodadFuncRatioRespawn"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_recover_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRecoverItem();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncRecoverItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_remove_instances";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRemoveInstance();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneId = reader.GetUInt32("zone_id");
                            _funcTemplates["DoodadFuncRemoveInstance"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_remove_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRemoveItem();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncRemoveItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_renew_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRenewItem();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id");
                            _funcTemplates["DoodadFuncRenewItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_require_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRequireItem();
                            func.Id = reader.GetUInt32("id");
                            func.WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _phaseFuncTemplates["DoodadFuncRequireItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_require_quests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRequireQuest();
                            func.Id = reader.GetUInt32("id");
                            func.WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id");
                            func.QuestId = reader.GetUInt32("quest_id");
                            _phaseFuncTemplates["DoodadFuncRequireQuest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_respawns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRespawn();
                            func.Id = reader.GetUInt32("id");
                            func.MinTime = reader.GetInt32("min_time");
                            func.MaxTime = reader.GetInt32("max_time");
                            _phaseFuncTemplates["DoodadFuncRespawn"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_rock_mines";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRockMine();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncRockMine"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_seed_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSeedCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSeedCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_shears";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncShear();
                            func.Id = reader.GetUInt32("id");
                            func.ShearTypeId = reader.GetUInt32("shear_type_id");
                            func.ShearTerm = reader.GetInt32("shear_term");
                            _funcTemplates["DoodadFuncShear"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_siege_periods";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSiegePeriod();
                            func.Id = reader.GetUInt32("id");
                            func.SiegePeriodId = reader.GetUInt32("siege_period_id");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            func.Defense = reader.GetBoolean("defense", true);
                            _phaseFuncTemplates["DoodadFuncSiegePeriod"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_signs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSign();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            func.PickNum = reader.GetInt32("pick_num");
                            _phaseFuncTemplates["DoodadFuncSign"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_skill_hits";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSkillHit();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id");
                            _funcTemplates["DoodadFuncSkillHit"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_skin_offs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSkinOff();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSkinOff"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_soil_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSoilCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSoilCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_spawn_gimmicks";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSpawnGimmick();
                            func.Id = reader.GetUInt32("id");
                            func.GimmickId = reader.GetUInt32("gimmick_id");
                            func.FactionId = reader.GetUInt32("faction_id");
                            func.Scale = reader.GetFloat("scale");
                            func.OffsetX = reader.GetFloat("offset_x");
                            func.OffsetY = reader.GetFloat("offset_y");
                            func.OffsetZ = reader.GetFloat("offset_z");
                            func.VelocityX = reader.GetFloat("velocity_x");
                            func.VelocityY = reader.GetFloat("velocity_y");
                            func.VelocityZ = reader.GetFloat("velocity_z");
                            func.AngleX = reader.GetFloat("angle_x");
                            func.AngleY = reader.GetFloat("angle_y");
                            func.AngleZ = reader.GetFloat("angle_z");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            _phaseFuncTemplates["DoodadFuncSpawnGimmick"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_spawn_mgmts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSpawnMgmt();
                            func.Id = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("group_id");
                            func.Spawn = reader.GetBoolean("spawn", true);
                            func.ZoneId = reader.GetUInt32("zone_id");
                            _phaseFuncTemplates["DoodadFuncSpawnMgmt"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_spice_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSpiceCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSpiceCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_stamp_makers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncStampMaker();
                            func.Id = reader.GetUInt32("id");
                            func.ConsumeMoney = reader.GetInt32("consume_money");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.ConsumeItemId = reader.GetUInt32("consume_item_id");
                            func.ConsumeCount = reader.GetInt32("consume_count");
                            _funcTemplates["DoodadFuncStampMaker"].Add(func.Id, func);
                        }
                    }
                }

                // TODO 1.2                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_store_uis";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncStoreUi();
                            func.Id = reader.GetUInt32("id");
                            func.MerchantPackId = reader.GetUInt32("merchant_pack_id");
                            _funcTemplates["DoodadFuncStoreUi"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_timers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncTimer();
                            func.Id = reader.GetUInt32("id");
                            func.Delay = reader.GetInt32("delay");
                            func.NextPhase = reader.GetInt32("next_phase", -1);
                            func.KeepRequester = reader.GetBoolean("keep_requester", true);
                            func.ShowTip = reader.GetBoolean("show_tip", true);
                            func.ShowEndTime = reader.GetBoolean("show_end_time", true);
                            func.Tip = reader.GetString("tip");
                            _phaseFuncTemplates["DoodadFuncTimer"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_tods";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncTod();
                            func.Id = reader.GetUInt32("id");
                            func.Tod = reader.GetInt32("tod");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1;
                            _phaseFuncTemplates["DoodadFuncTod"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_tree_byproducts_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncTreeByProductsCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncTreeByProductsCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ucc_imprints";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncUccImprint();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncUccImprint"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_uses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncUse();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id", 0);
                            _funcTemplates["DoodadFuncUse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_water_volumes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncWaterVolume();
                            func.Id = reader.GetUInt32("id");
                            func.LevelChange = reader.GetFloat("levelChange");
                            func.Duration = reader.GetFloat("duration");
                            _phaseFuncTemplates["DoodadFuncWaterVolume"].Add(func.Id, func);
                        }
                    }
                }

                // TODO 1.2                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_zone_reacts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncZoneReact();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneGroupId = reader.GetUInt32("zone_group_id");
                            func.NextPhase = reader.GetInt32("next_phase", -1);

                            _phaseFuncTemplates["DoodadFuncZoneReact"].Add(func.Id, func);
                        }
                    }
                }

                _log.Info("Finished loading doodad functions ...");
               
                #endregion
                
                #region doodads_and_func_groups
                
                _log.Info("Loading doodad templates...");

                // First load all doodad_func_groups
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_groups";
                    command.Prepare();
                    using (var sqliteDataReaderChild = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReaderChild))
                    {
                        while (reader.Read())
                        {
                            var funcGroups = new DoodadFuncGroups();
                            funcGroups.Id = reader.GetUInt32("id");
                            funcGroups.Almighty = reader.GetUInt32("doodad_almighty_id");
                            funcGroups.GroupKindId = (DoodadFuncGroups.DoodadFuncGroupKind)reader.GetUInt32("doodad_func_group_kind_id");
                            funcGroups.SoundId = reader.GetUInt32("sound_id", 0);
                            
                            if (!_allFuncGroups.TryAdd(funcGroups.Id, funcGroups))
                                _log.Fatal($"Failed to add FuncGroups: {funcGroups.Id}");
                        }
                    }
                }
                
                // Then Load actual doodads
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * from doodad_almighties";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var templateId = reader.GetUInt32("id");
                            
                            var cofferCapacity = IsCofferTemplate(templateId);
                            
                            DoodadTemplate template;
                            if (cofferCapacity > 0)
                                template = new DoodadCofferTemplate() { Capacity = cofferCapacity };
                            else
                                template = new DoodadTemplate();
                            
                            template.Id = templateId;
                            template.OnceOneMan = reader.GetBoolean("once_one_man", true);
                            template.OnceOneInteraction = reader.GetBoolean("once_one_interaction", true);
                            template.MgmtSpawn = reader.GetBoolean("mgmt_spawn", true);
                            template.Percent = reader.GetInt32("percent", 0);
                            template.MinTime = reader.GetInt32("min_time", 0);
                            template.MaxTime = reader.GetInt32("max_time", 0);
                            template.ModelKindId = reader.GetUInt32("model_kind_id");
                            template.UseCreatorFaction = reader.GetBoolean("use_creator_faction", true);
                            template.ForceTodTopPriority = reader.GetBoolean("force_tod_top_priority", true);
                            template.MilestoneId = reader.GetUInt32("milestone_id", 0);
                            template.GroupId = reader.GetUInt32("group_id");
                            template.UseTargetDecal = reader.GetBoolean("use_target_decal", true);
                            template.UseTargetSilhouette = reader.GetBoolean("use_target_silhouette", true);
                            template.UseTargetHighlight = reader.GetBoolean("use_target_highlight", true);
                            template.TargetDecalSize = reader.GetFloat("target_decal_size", 0);
                            template.SimRadius = reader.GetInt32("sim_radius", 0);
                            template.CollideShip = reader.GetBoolean("collide_ship", true);
                            template.CollideVehicle = reader.GetBoolean("collide_vehicle", true);
                            template.ClimateId = reader.GetUInt32("climate_id", 0);
                            template.SaveIndun = reader.GetBoolean("save_indun", true);
                            template.ForceUpAction = reader.GetBoolean("force_up_action", true);
                            template.Parentable = reader.GetBoolean("parentable", true);
                            template.Childable = reader.GetBoolean("childable", true);
                            template.FactionId = reader.GetUInt32("faction_id");
                            template.GrowthTime = reader.GetInt32("growth_time", 0);
                            template.DespawnOnCollision = reader.GetBoolean("despawn_on_collision", true);
                            template.NoCollision = reader.GetBoolean("no_collision", true);
                            template.RestrictZoneId = reader.IsDBNull("restrict_zone_id") ? 0 : reader.GetUInt32("restrict_zone_id");

                            _templates.Add(template.Id, template);
                        }
                    }
                }                
                
                // Bind FuncGroups to Template
                foreach (var (_, funcGroups) in _allFuncGroups)
                {
                    var template = GetTemplate(funcGroups.Almighty);
                    if (template != null)
                        template.FuncGroups.Add(funcGroups);
                }

                _log.Info("Loaded {0} doodad templates", _templates.Count);
                #endregion
            }
                        
            _loaded = true;
        }

        /// <summary>
        /// Checks if a DoodadTemplateId has a doodad_func_coffer attached to it
        /// </summary>
        /// <param name="templateId"></param>
        /// <returns>Returns the Coffer Capacity if true, otherwise returns -1</returns>
        public int IsCofferTemplate(uint templateId)
        {
            if (templateId == 0)
                return -1;
            
            // Check if template is a Coffer
            foreach (var (_, funcGroup) in _allFuncGroups)
            {
                if (funcGroup.Almighty != templateId)
                    continue;
                if (_funcsByGroups.TryGetValue(funcGroup.Id, out var funcList))
                {
                    foreach (var func in funcList)
                    {
                        if (_phaseFuncTemplates.TryGetValue(func.FuncType, out var phaseFuncTemplates))
                            if (phaseFuncTemplates.TryGetValue(func.FuncId, out var phaseFuncTemplate))
                                if (phaseFuncTemplate is DoodadFuncCoffer funcCoffer)
                                {
                                    return funcCoffer.Capacity;
                                }
                    }
                }
            }

            return -1;
        }

        public Doodad Create(uint bcId, uint id, GameObject obj = null)
        {
            if (!_templates.ContainsKey(id))
                return null;
            var template = _templates[id];
            Doodad doodad = null;

            // Check if template is a Coffer
            if (template is DoodadCofferTemplate doodadCofferTemplate)
                doodad = new DoodadCoffer { Capacity = doodadCofferTemplate.Capacity };

            if (doodad == null)
                doodad = new Doodad();
            
            doodad.ObjId = bcId > 0 ? bcId : ObjectIdManager.Instance.GetNextId();
            doodad.TemplateId = template.Id; // duplicate Id
            doodad.Template = template;
            doodad.OwnerObjId = obj?.ObjId ?? 0;
            doodad.PlantTime = DateTime.UtcNow;
            doodad.OwnerType = DoodadOwnerType.System;
            doodad.FuncGroupId = doodad.GetFuncGroupId();

            switch (obj)
            {
                case Character character:
                    doodad.OwnerId = character.Id;
                    doodad.OwnerType = DoodadOwnerType.Character;
                    break;
                case House house:
                    doodad.OwnerObjId = 0;
                    doodad.ParentObjId = house.ObjId;
                    doodad.OwnerId = house.OwnerId;
                    doodad.OwnerType = DoodadOwnerType.Housing;
                    doodad.DbHouseId = house.Id;
                    break;
                case Transfer transfer:
                    doodad.OwnerId = 0;
                    doodad.ParentObjId = transfer.ObjId;
                    doodad.OwnerType = DoodadOwnerType.System;
                    break;
            }

            Task.Run(() => doodad.InitDoodad());
            
            //_log.Debug($"Create: TemplateId {doodad.TemplateId}, ObjId {doodad.ObjId}, FuncGroupId {doodad.FuncGroupId}");
            
            return doodad;
        }

        public DoodadFunc GetFunc(uint funcId)
        {
            if (!_funcsById.ContainsKey(funcId))
                return null;
            return _funcsById[funcId];
        }

        public DoodadFunc GetFunc(uint funcGroupId, uint skillId)
        {
            if (!_funcsByGroups.ContainsKey(funcGroupId))
                return null;
            foreach (var func in _funcsByGroups[funcGroupId])
            {
                if (func.SkillId == skillId)
                    return func;
            }

            // сначала пропускаем функции с NextPhase = -1
            foreach (var func in _funcsByGroups[funcGroupId])
            {
                if (func.SkillId == 0 && func.NextPhase != -1)
                    return func;
            }

            // затем ищем и с NextPhase = -1
            foreach (var func in _funcsByGroups[funcGroupId])
            {
                if (func.SkillId == 0)
                    return func;
            }

            return null;
        }

        public List<DoodadFunc> GetFuncsForGroup(uint funcGroupId)
        {
            if (_funcsByGroups.ContainsKey(funcGroupId))
                return _funcsByGroups[funcGroupId];
            return new List<DoodadFunc>();
        }

        public DoodadPhaseFunc[] GetPhaseFunc(uint funcGroupId)
        {
            return _phaseFuncs.ContainsKey(funcGroupId) ? _phaseFuncs[funcGroupId].ToArray() : Array.Empty<DoodadPhaseFunc>();
        }

        public DoodadFuncTemplate GetFuncTemplate(uint funcId, string funcType)
        {
            if (!_funcTemplates.ContainsKey(funcType))
                return null;
            var funcs = _funcTemplates[funcType];
            if (funcs.ContainsKey(funcId))
                return funcs[funcId];
            return null;
        }

        public DoodadPhaseFuncTemplate GetPhaseFuncTemplate(uint funcId, string funcType)
        {
            if (!_phaseFuncTemplates.ContainsKey(funcType))
                return null;
            var funcs = _phaseFuncTemplates[funcType];
            if (funcs.ContainsKey(funcId))
                return funcs[funcId];
            return null;
        }


        /// <summary>
        /// GetDoodadFuncGroups - Get a group of functions for a given TemplateId
        /// </summary>
        /// <param name="doodadTemplateId"></param>
        /// <returns>List<DoodadFuncGroups></returns>
        public List<DoodadFuncGroups> GetDoodadFuncGroups(uint doodadTemplateId)
        {
            var listDoodadFuncGroups = new List<DoodadFuncGroups>();

            if (_templates.ContainsKey(doodadTemplateId))
            {
                var doodaTemplates = _templates[doodadTemplateId];
                listDoodadFuncGroups.AddRange(doodaTemplates.FuncGroups);
            }
            return listDoodadFuncGroups;
        }

        public List<uint> GetDoodadFuncGroupsId(uint doodadTemplateId)
        {
            var listId = new List<uint>();

            var listDoodadFuncGroups = new List<DoodadFuncGroups>();

            if (_templates.ContainsKey(doodadTemplateId))
            {
                var doodaTemplates = _templates[doodadTemplateId];
                listDoodadFuncGroups.AddRange(doodaTemplates.FuncGroups);
                foreach (var item in listDoodadFuncGroups)
                {
                    listId.Add(item.Id);
                }
            }
            return listId;
        }

        /// <summary>
        /// GetDoodadFuncs - Get all features
        /// </summary>
        /// <param name="doodadFuncGroupId"></param>
        /// <returns>List<DoodadFunc></returns>
        public List<DoodadFunc> GetDoodadFuncs(uint doodadFuncGroupId)
        {
            if (_funcsByGroups.ContainsKey(doodadFuncGroupId))
                return _funcsByGroups[doodadFuncGroupId];
            return new List<DoodadFunc>();
        }
        /// <summary>
        /// GetDoodadPhaseFuncs - Получить все фазовые функции
        /// </summary>
        /// <param name="funcGroupId"></param>
        /// <returns>DoodadFunc[]</returns>
        public DoodadPhaseFunc[] GetDoodadPhaseFuncs(uint funcGroupId)
        {
            if (_phaseFuncs.ContainsKey(funcGroupId))
                return _phaseFuncs[funcGroupId].ToArray();
            return Array.Empty<DoodadPhaseFunc>();
        }

        /// <summary>
        /// Saves and creates a doodad 
        /// </summary>
        public Doodad CreatePlayerDoodad(Character character, uint id, float x, float y, float z, float zRot, float scale, ulong itemId)
        {
            _log.Warn("{0} is placing a doodad {1} at position {2} {3} {4}", character.Name, id, x, y, z);

            var targetHouse = HousingManager.Instance.GetHouseAtLocation(x, y);
            var usedItem = character.Inventory.Bag.GetItemByItemId(itemId);

            // Create doodad
            var doodad = Instance.Create(0, id, character);
            doodad.IsPersistent = true;
            doodad.Transform = character.Transform.CloneDetached(doodad);
            doodad.Transform.Local.SetPosition(x, y, z);
            doodad.Transform.Local.SetZRotation(zRot);
            doodad.ItemId = itemId;
            doodad.PlantTime = DateTime.UtcNow;

            if (targetHouse != null)
            {
                doodad.DbHouseId = targetHouse.Id;
                doodad.AttachPoint = AttachPointKind.None;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.ParentObj = targetHouse;
                doodad.ParentObjId = targetHouse.ObjId;
                
                _log.Debug(targetHouse.Template.AutoZOffsetY);
                
                doodad.Transform.SetParent(targetHouse.Transform, !(usedItem.Template.Category_Id == 51 || usedItem.Template.Category_Id == 8));
            }
            else
            {
                doodad.DbHouseId = 0;
            }
            
            if (scale > 0f)
                doodad.SetScale(scale);

            // Consume item
            var items = ItemManager.Instance.GetItemIdsFromDoodad(id);
            
            doodad.ItemTemplateId = usedItem.TemplateId;

            if (doodad is DoodadCoffer coffer)
            {
                coffer.InitializeCoffer(character.Id);
            }

            foreach (var item in items)
                character.Inventory.ConsumeItem(new[] { SlotType.Inventory }, ItemTaskType.DoodadCreate, item, 1, usedItem);

            doodad.Spawn();
            doodad.Save();

            return doodad;
        }
        
      

        public bool OpenCofferDoodad(Character character, uint objId)
        {
            var doodad = WorldManager.Instance.GetDoodad(objId);
            if (!(doodad is DoodadCoffer coffer))
                return false;
            
            // Somebody already using this ?
            if (coffer.OpenedBy != null)
                return false;
            
            // TODO: Check permissions

            coffer.OpenedBy = character;

            if (character != null)
            {
                byte firstSlot = 0;
                while (firstSlot < coffer.Capacity)
                {
                    character.SendPacket(new SCCofferContentsUpdatePacket(coffer, firstSlot));
                    firstSlot += SCCofferContentsUpdatePacket.MaxSlotsToSend;
                }
            }

            return true;
        }

        public bool CloseCofferDoodad(Character character, uint objId)
        {
            var doodad = WorldManager.Instance.GetDoodad(objId);
            if (!(doodad is DoodadCoffer coffer))
                return false;

            // Used for GM commands
            if (character == null)
            {
                coffer.OpenedBy = null;
                return true;
            }

            // Only the person who opened it, can close it
            if ((coffer.OpenedBy != null) && (coffer.OpenedBy?.Id != character?.Id))
                return false;

            coffer.OpenedBy = null;

            return true;
        }

        public bool ChangeDoodadData(Character player, Doodad doodad, int data)
        {
            // TODO: Can non-coffer doodads that use this packet only be changed by their owner ?
            if (doodad.OwnerId != player.Id)
                return false;
            
            // For Coffers validate if select option is applicable
            if (doodad is DoodadCoffer coffer)
            {
                if ((data == (int)HousingPermission.Family) && (player.Family <= 0))
                {
                    player.SendErrorMessage(ErrorMessageType.FamilyNotExist); // Not sure 
                    return false;
                }
                if ((data == (int)HousingPermission.Guild) && ((player.Expedition == null) || (player.Expedition?.Id <= 0)))
                {
                    player.SendErrorMessage(ErrorMessageType.OnlyExpeditionMember); // Not sure
                    return false;
                }
            }
            
            doodad.Data = data;

            doodad.BroadcastPacket(new SCDoodadChangedPacket(doodad.ObjId, doodad.Data), false);
            
            return true;
        }
        // }

    }
}

using System.Collections.Concurrent;
using System.Data;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.Crime;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CrimeManager() : Singleton<CrimeManager>, ICrimeManager
{
    public const int WantedCrimePointThreshold = 50;
    public const int PirateCrimePointThreshold = 3000;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private ConcurrentDictionary<ulong, CrimeEvent> CrimeEvents { get; } = [];
    // ReSharper disable once CollectionNeverUpdated.Local
    private List<ulong> DeletedEventIds { get; } = []; // Later needed for trial and evidence tempering skills
    private List<ulong> UpdatedEventIds { get; } = [];
    private ConcurrentDictionary<uint,HashSet<uint>> ReportedSuspects { get; } = [];

    /// <summary>
    /// Gets a list of evidence reports for this player. 
    /// </summary>
    /// <param name="playerId"></param>
    /// <param name="includeOld">If true, also includes crimes that have already been judged</param>
    /// <returns></returns>
    public List<CrimeEvent> GetCrimesOfPlayer(uint playerId, bool includeOld)
    {
        return includeOld
            ? CrimeEvents.Values.Where(x => x.Criminal == playerId).ToList()
            : CrimeEvents.Values.Where(x => x.Criminal == playerId && x.JudgementTime <= DateTime.MinValue).ToList();
    }

    public void Load()
    {
        Logger.Info("Loading crime events...");
        CrimeEvents.Clear();
        DeletedEventIds.Clear();
        UpdatedEventIds.Clear();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM crime";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var crimeEvent = new CrimeEvent()
            {
                Id = reader.GetUInt32("id"),
                Criminal = reader.GetUInt32("criminal"),
                Victim = reader.GetUInt32("victim"),
                Reporter = reader.GetUInt32("reporter"),
                DoodadTemplate = reader.GetUInt32("doodad_template"),
                CrimeKind = (CrimeKind)reader.GetUInt32("crime_type"),
                ZoneKey = reader.GetUInt32("zone_key"),
                Position = new Vector3(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z")),
                CrimeTime = reader.GetDateTime("crime_time"),
                ReportTime = reader.GetDateTime("report_time"),
                UsedSkillId = reader.GetUInt32("skill_id"),
                DoodadNextFuncGroup = reader.GetUInt32("next_func_group"),
                DoodadFuncId = reader.GetUInt32("func_id"),
                Msg = reader.GetString("msg"),
                JudgementTime = reader.IsDBNull(reader.GetOrdinal("judgement_time")) ? DateTime.MinValue : reader.GetDateTime("judgement_time"),
            };
            if (!CrimeEvents.TryAdd(crimeEvent.Id, crimeEvent))
                Logger.Warn($"Was not able to load Crime Event {crimeEvent.Id}");
        }
    }

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deleteCount = 0;
        var updateCount = 0;
        // Remove deleted items from DB
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;
            lock (DeletedEventIds)
            {
                if (DeletedEventIds.Count > 0)
                {
                    using (var deleteCommand = connection.CreateCommand())
                    {
                        var removedItemList = string.Join(",", DeletedEventIds);
                        deleteCommand.CommandText = $"DELETE FROM crime WHERE `id` IN({removedItemList})";
                        deleteCommand.Prepare();
                        deleteCount += deleteCommand.ExecuteNonQuery();
                    }

                    if (deleteCount != DeletedEventIds.Count)
                        Logger.Error($"Some crime events could not be deleted, only {deleteCount}/{DeletedEventIds.Count} events removed !");
                    DeletedEventIds.Clear();
                }
            }
        }

        // Update Container Info
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            lock (UpdatedEventIds)
            {
                foreach (var c in UpdatedEventIds.ToArray())
                {
                    var crimeEvent = CrimeEvents.GetValueOrDefault(c);
                    if (crimeEvent == null)
                        continue;

                    command.CommandText =
                        "REPLACE INTO crime (" +
                        "`id`,`criminal`,`victim`,`reporter`,`doodad_template`,`crime_type`,`zone_key`,`x`,`y`,`z`," +
                        "`crime_time`,`report_time`,`skill_id`,`next_func_group`,`func_id`,`msg`,`judgement_time`" +
                        ") VALUES ( " +
                        "@id, @criminal, @victim, @reporter, @doodad_template, @crime_type, @zone_key, @x, @y, @z," +
                        " @crime_time, @report_time, @arg1, @arg2, @arg3, @msg, @judgement_time" +
                        ")";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@id", crimeEvent.Id);
                    command.Parameters.AddWithValue("@criminal", crimeEvent.Criminal);
                    command.Parameters.AddWithValue("@victim", crimeEvent.Victim);
                    command.Parameters.AddWithValue("@reporter", crimeEvent.Reporter);
                    command.Parameters.AddWithValue("@doodad_template", crimeEvent.DoodadTemplate);
                    command.Parameters.AddWithValue("@crime_type", crimeEvent.CrimeKind);
                    command.Parameters.AddWithValue("@zone_key", crimeEvent.ZoneKey);
                    command.Parameters.AddWithValue("@x", crimeEvent.Position.X);
                    command.Parameters.AddWithValue("@y", crimeEvent.Position.Y);
                    command.Parameters.AddWithValue("@z", crimeEvent.Position.Z);
                    command.Parameters.AddWithValue("@crime_time", crimeEvent.CrimeTime);
                    command.Parameters.AddWithValue("@report_time", crimeEvent.ReportTime);
                    command.Parameters.AddWithValue("@arg1", crimeEvent.UsedSkillId);
                    command.Parameters.AddWithValue("@arg2", crimeEvent.DoodadNextFuncGroup);
                    command.Parameters.AddWithValue("@arg3", crimeEvent.DoodadFuncId);
                    command.Parameters.AddWithValue("@msg", crimeEvent.Msg);
                    command.Parameters.AddWithValue("@judgement_time", crimeEvent.JudgementTime);
                    try
                    {
                        var res = command.ExecuteNonQuery();
                        updateCount += res;
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
            }
        }

        return (updateCount, deleteCount);
    }

    /// <summary>
    /// Reports a crime for a given evidence
    /// </summary>
    /// <param name="reporter"></param>
    /// <param name="evidence"></param>
    /// <param name="usedSkillId"></param>
    /// <param name="doodadNextFuncGroup"></param>
    /// <param name="doodadFuncId"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public CrimeEvent ReportCrime(Character reporter, Doodad evidence, uint usedSkillId, int doodadNextFuncGroup, uint doodadFuncId, string message)
    {
        if (evidence.OwnerType == DoodadOwnerType.Character && evidence.OwnerId == reporter.Id)
        {
            Logger.Warn($"{reporter.Name} ({reporter.Id}) tried to somehow report their own crimes at {evidence.Transform.World.Position}");
            SusManager.Instance.LogActivity(SusManager.CategoryCheating, reporter, $"Reported own crime at {evidence.Transform.World.Position} (type={evidence.TemplateId}");
            return null;
        }
        var crimeType = CrimeKind.Invalid;
        short crimeValue = 0; // Not sure if needed to be known here
        var nextPhase = 0;
        foreach (var evidenceCurrentFunc in evidence.CurrentFuncs)
        {
            if (evidenceCurrentFunc.FuncType == "DoodadFuncEvidenceItemLoot")
            {
                var func = DoodadManager.Instance.GetFuncTemplate(evidenceCurrentFunc.FuncId, evidenceCurrentFunc.FuncType);
                if (func is DoodadFuncEvidenceItemLoot evidenceItemLoot)
                {
                    crimeType = (CrimeKind)evidenceItemLoot.CrimeKindId;
                    crimeValue = evidenceItemLoot.CrimeValue;
                    nextPhase = evidenceCurrentFunc.NextPhase;
                }
            }
        }

        var zoneKey = ZoneManager.Instance.GetZoneByKey(evidence.Transform.ZoneId);

        var newId = CrimeIdManager.Instance.GetNextId();
        var newEvent = new CrimeEvent()
        {
            Id = newId,
            Criminal = evidence.OwnerId,
            Victim = (uint)evidence.Data,
            Reporter = reporter.Id,
            DoodadTemplate = evidence.ItemTemplateId,
            CrimeKind = crimeType,
            CrimeTime = evidence.PlantTime,
            ReportTime = DateTime.UtcNow,
            Position = evidence.Transform.World.Position,
            ZoneKey = zoneKey?.Id ?? 0u,
            UsedSkillId = usedSkillId,
            DoodadNextFuncGroup = (uint)doodadNextFuncGroup,
            DoodadFuncId = doodadFuncId,
            Msg = message
        };

        if (!CrimeEvents.TryAdd(newEvent.Id, newEvent))
        {
            CrimeIdManager.Instance.ReleaseId(newId); // Free the Id again if not able to add
            Logger.Warn($"Unable to report crime at {newEvent.Position} with message {newEvent.Msg}");
            return null;
        }
        UpdatedEventIds.Add(newEvent.Id);

        // TODO: Handle this phase change by doing the skill, and handling the crime points in DoodadFuncEvidenceItemLoot of the doodad.
        // Add crime points to criminal
        AddCrimePoints(newEvent.Criminal, crimeType, crimeValue);

        // Try to add this to ongoing trials
        TrialManager.Instance.AddNewEvidence(newEvent);

        // Progress Doodad to next phase to finish the report
        if (nextPhase > 0)
        {
            evidence.DoChangePhase(reporter, nextPhase);
        }

        // Return the new CrimeId
        return newEvent;
    }

    /// <summary>
    /// Add crime points to a player
    /// </summary>
    /// <param name="criminalId"></param>
    /// <param name="crimeType"></param>
    /// <param name="crimePoints"></param>
    private void AddCrimePoints(uint criminalId, CrimeKind crimeType, short crimePoints)
    {
        // TODO: Handle this in the character manager instead so it can do offline people as well?
        // TODO: Handle this in DoodadFuncEvidenceItemLoot of the doodad.
        Logger.Debug($"Adding {crimePoints} crime points to player {criminalId} for {crimeType} ({(int)crimeType})");
        var criminal = WorldManager.Instance.GetCharacterById(criminalId);
        if (criminal is { IsOnline: true })
        {
            criminal.AddCrime(crimePoints);
            criminal.EvidenceReportedCount++;
        }
        else
        {
            CharacterManager.AddOfflineCrimePoints(criminalId, crimePoints);
        }
    }

    /// <summary>
    /// Creates crime evidence
    /// </summary>
    /// <param name="criminal"></param>
    /// <param name="evidenceDoodadTemplateId"></param>
    /// <param name="targetLocation"></param>
    /// <param name="targetRotation"></param>
    /// <param name="victimPlayerId"></param>
    /// <param name="sourceDoodadTemplateId"></param>
    /// <returns></returns>
    private Doodad GenerateEvidence(Character criminal, uint evidenceDoodadTemplateId, Vector3 targetLocation, Vector3 targetRotation, uint victimPlayerId, uint sourceDoodadTemplateId)
    {
        // TODO: floor/water surface the bloodstain Z height?
        return DoodadManager.Instance.CreatePlayerDoodad(criminal, evidenceDoodadTemplateId,
            targetLocation.X, targetLocation.Y, targetLocation.Z, targetRotation.Z, 1f, 0, FarmType.Invalid, sourceDoodadTemplateId, (int)victimPlayerId, true);
    }

    /// <summary>
    /// Generate small bloodstain
    /// </summary>
    /// <param name="criminal"></param>
    /// <param name="victim"></param>
    /// <returns></returns>
    public Doodad GenerateEvidenceFromDamage(BaseUnit criminal, Unit victim)
    {
        var criminalPlayer = criminal.GetOwnerCharacter();
        var victimPlayer = victim.GetOwnerCharacter();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (criminal is null)
            return null;

        /*
        // Check if there is already a small bloodstain nearby with the same data
        var nearbyDoodads = WorldManager.GetAround<Doodad>(victim).ToArray();
        foreach (var doodad in nearbyDoodads)
        {
            if (doodad.TemplateId != DoodadConstants.SmallBloodstain)
                continue;
            if (doodad.GetDistanceTo(victim, true) > 1f)
                continue;
            if (doodad.OwnerId == criminalPlayer.Id && doodad.Data == (victimPlayer?.Id ?? 0))
                return null;
        }
        */

        return GenerateEvidence(criminalPlayer,
            DoodadConstants.SmallBloodstain,
            victimPlayer?.Transform.World.Position ?? criminal.Transform.World.Position,
            victimPlayer?.Transform.World.Rotation ?? Vector3.Zero,
            victimPlayer?.Id ?? 0,
            0);
    }

    /// <summary>
    /// Generate large bloodstain
    /// </summary>
    /// <param name="criminal"></param>
    /// <param name="victim"></param>
    /// <returns></returns>
    public Doodad GenerateEvidenceFromKill(BaseUnit criminal, Unit victim)
    {
        var criminalPlayer = criminal.GetOwnerCharacter();
        var victimPlayer = victim.GetOwnerCharacter();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (criminal is null)
            return null;

        return GenerateEvidence(criminalPlayer,
            DoodadConstants.LargeBloodstain,
            victimPlayer?.Transform.World.Position ?? criminal.Transform.World.Position,
            victimPlayer?.Transform.World.Rotation ?? Vector3.Zero,
            victimPlayer?.Id ?? 0,
            0);
    }

    /// <summary>
    /// Generate footprints
    /// </summary>
    /// <param name="criminal"></param>
    /// <param name="stolenDoodad"></param>
    /// <returns></returns>
    public Doodad GenerateEvidenceFromTheft(Character criminal, Doodad stolenDoodad)
    {
        if (stolenDoodad.OwnerType != DoodadOwnerType.Character)
            return null;
        if (criminal is null)
            return null;

        return GenerateEvidence(criminal,
            criminal.Gender == Gender.Female ? DoodadConstants.FootprintFemale : DoodadConstants.FootprintMale,
            stolenDoodad.Transform.World.Position, stolenDoodad.Transform.World.Rotation,
            stolenDoodad.OwnerId, stolenDoodad.TemplateId);
    }

    public void ArchiveEvidence(CrimeEvent crimeEvent, DateTime time)
    {
        crimeEvent.JudgementTime = time;
        UpdatedEventIds.Add(crimeEvent.Id);
    }

    public void RemoveEvidence(CrimeEvent crimeEvent)
    {
        if (CrimeEvents.Remove(crimeEvent.Id, out var removed))
            DeletedEventIds.Add(removed.Id);
    }

    /// <summary>
    /// Handle bot report skill
    /// </summary>
    /// <param name="bot"></param>
    /// <param name="reporter"></param>
    /// <param name="message"></param>
    /// <returns>True if report was successful</returns>
    public bool ReportBot(Character bot, Character reporter, string message)
    {
        if (!ReportedSuspects.TryGetValue(bot.Id, out var reportedSuspectList))
        {
            reportedSuspectList = [];
            ReportedSuspects.TryAdd(bot.Id, reportedSuspectList);
        }

        if (reportedSuspectList.Add(reporter.Id))
        {
            SusManager.Instance.LogActivity(SusManager.CategoryBotReport, bot, $"Possible bot {bot.Name} ({bot.Id}) got reported by {reporter.Name} ({reporter.Id}), Msg: {message}");

            // Add sus buff
            if (!bot.Buffs.CheckBuff((uint)BuffConstants.SuspectedUser))
            {
                bot.Buffs.AddBuff((uint)BuffConstants.SuspectedUser, reporter);
            }

            // If enough reports, add prime sus buff
            if (reportedSuspectList.Count >= AppConfiguration.Instance.Justice.BotReportPrimeSuspectCount)
            {
                if (!bot.Buffs.CheckBuffTag((uint)BuffConstants.TagSuspects))
                {
                    bot.Buffs.AddBuff((uint)BuffConstants.TransformingIntoPrimeSuspect, reporter);
                }

                if (reportedSuspectList.Count == AppConfiguration.Instance.Justice.BotReportPrimeSuspectCount)
                {
                    SusManager.Instance.LogActivity(SusManager.CategoryBot, bot, $"Possible bot {bot.Name} ({bot.Id}) has reached the report threshold of {AppConfiguration.Instance.Justice.BotReportPrimeSuspectCount}");
                }
            }
            reporter.BotReportedCount++;
            bot.ReportedAsBotCount++;
            return true;
        }

        return false;
    }

    public bool ReportBotExpired(Character bot)
    {
        if (!ReportedSuspects.TryGetValue(bot.Id, out var reportedSuspectList))
        {
            return true; // Does not have a report list
        }

        // Check if prime suspect
        if (bot.Buffs.CheckBuffTag((uint)BuffConstants.TagSuspects))
        {
            bot.Buffs.RemoveBuffs(BuffKind.Bad, 10, (uint)BuffConstants.TagSuspects);
            reportedSuspectList.Clear();
            SusManager.Instance.LogActivity(SusManager.CategoryBot, bot, $"Possible bot {bot} ({bot.Id}) has cleared their Prime Suspect status by talking to a judge.");
            return true;
        }

        // No buffs found
        return false;
    }
}

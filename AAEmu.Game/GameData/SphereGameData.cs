using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Spheres;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class SphereGameData : Singleton<SphereGameData>, IGameDataLoader
{
    private Dictionary<uint, Spheres> _spheres;
    private Dictionary<uint, SphereQuests> _sphereQuests;
    private Dictionary<uint, SphereSkills> _sphereSkills;
    private Dictionary<uint, SphereSounds> _sphereSounds;
    private Dictionary<uint, SphereDoodadInteracts> _sphereDoodadInteracts;
    private Dictionary<uint, SphereChatBubbles> _sphereChatBubbles;
    private Dictionary<uint, SphereBuffs> _sphereBuffs;
    private Dictionary<uint, SphereBubbles> _sphereBubbles;
    private Dictionary<uint, SphereAcceptQuests> _sphereAcceptQuests;
    private Dictionary<uint, SphereAcceptQuestQuests> _sphereAcceptQuestQuests;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _spheres = new Dictionary<uint, Spheres>();
        _sphereQuests = new Dictionary<uint, SphereQuests>();
        _sphereSkills = new Dictionary<uint, SphereSkills>();
        _sphereSounds = new Dictionary<uint, SphereSounds>();
        _sphereDoodadInteracts = new Dictionary<uint, SphereDoodadInteracts>();
        _sphereChatBubbles = new Dictionary<uint, SphereChatBubbles>();
        _sphereBuffs = new Dictionary<uint, SphereBuffs>();
        _sphereBubbles = new Dictionary<uint, SphereBubbles>();
        _sphereAcceptQuests = new Dictionary<uint, SphereAcceptQuests>();
        _sphereAcceptQuestQuests = new Dictionary<uint, SphereAcceptQuestQuests>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM spheres";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new Spheres()
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name"),
                        EnterOrLeave = reader.GetBoolean("enter_or_leave"),
                        SphereDetailId = reader.GetUInt32("sphere_detail_id"),
                        SphereDetailType = reader.GetString("sphere_detail_type"),
                        TriggerConditionId = (AreaSphereTriggerCondition)reader.GetUInt32("trigger_condition_id"),
                        TriggerConditionTime = reader.GetUInt32("trigger_condition_time", 0),
                        TeamMsg = reader.GetString("team_msg"),
                        CategoryId = reader.GetUInt32("category_id"),
                        OrUnitReqs = reader.GetBoolean("or_unit_reqs"),
                        IsPersonalMsg = reader.GetBoolean("is_personal_msg"),
                        //template.MilestoneId = reader.GetUInt32("milestone_id"); // there is no such field in the database for version 3.0.3.0
                        //template.NameTr = reader.GetBoolean("name_tr"); // there is no such field in the database for version 3.0.3.0
                        //template.TeamMsgTr = reader.GetBoolean("team_msg_tr"); // there is no such field in the database for version 3.0.3.0
                    };

                    _spheres.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereQuests()
                    {
                        Id = reader.GetUInt32("id"),
                        QuestId = reader.GetUInt32("quest_id"),
                        QuestTriggerId = (QuestTrigger)reader.GetUInt32("quest_trigger_id")
                    };

                    _sphereQuests.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_skills";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereSkills()
                    {
                        Id = reader.GetUInt32("id"),
                        SkillId = reader.GetUInt32("skill_id"),
                        MaxRate = reader.GetUInt32("max_rate"),
                        MinRate = reader.GetUInt32("min_rate")
                    };

                    _sphereSkills.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_sounds";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereSounds()
                    {
                        Id = reader.GetUInt32("id"),
                        SoundId = reader.GetUInt32("sound_id"),
                        Broadcast = reader.GetBoolean("broadcast")
                    };

                    _sphereSounds.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_doodad_interacts";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereDoodadInteracts()
                    {
                        Id = reader.GetUInt32("id"),
                        SkillId = reader.GetUInt32("skill_id"),
                        DoodadFamilyId = reader.GetUInt32("doodad_family_id")
                    };

                    _sphereDoodadInteracts.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_chat_bubbles";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereChatBubbles()
                    {
                        Id = reader.GetUInt32("id"),
                        SphereBubbleId = reader.GetUInt32("sphere_bubble_id"),
                        IsStart = reader.GetBoolean("is_start"),
                        Speech = reader.GetString("speech"),
                        NpcId = reader.GetUInt32("npc_id", 0),
                        NpcSpawnerId = reader.GetUInt32("npc_spawner_id", 0),
                        NextBubble = reader.GetUInt32("next_bubble", 0),
                        SoundId = reader.GetUInt32("sound_id", 0),
                        Angle = reader.GetUInt32("angle", 0),
                        ChatBubbleKindId = (ChatBubbleKind)reader.GetUInt32("chat_bubble_kind_id"),
                        Facial = reader.GetString("facial", ""),
                        CameraId = reader.GetUInt32("camera_id", 0),
                        ChangeSpeakerName = reader.GetString("change_speaker_name", "")
                    };

                    _sphereChatBubbles.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_buffs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereBuffs()
                    {
                        Id = reader.GetUInt32("id"),
                        BuffId = reader.GetUInt32("buff_id")

                    };

                    _sphereBuffs.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_bubbles";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereBubbles()
                    {
                        Id = reader.GetUInt32("id")
                    };

                    _sphereBubbles.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_accept_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereAcceptQuests()
                    {
                        Id = reader.GetUInt32("id")
                    };

                    _sphereAcceptQuests.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM sphere_accept_quest_quests";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new SphereAcceptQuestQuests()
                    {
                        Id = reader.GetUInt32("id"),
                        SphereAcceptQuestId = reader.GetUInt32("sphere_accept_quest_id"),
                        QuestId = reader.GetUInt32("quest_id")
                    };

                    _sphereAcceptQuestQuests.Add(template.Id, template);
                }
            }
        }
    }

    public static List<string> GetQuestSphere(uint questId)
    {
        var sphereIds = new List<string>();

        var questTemplate = QuestManager.Instance.GetTemplate(questId);
        if (questTemplate == null)
            return null;
        foreach (QuestComponentKind step in Enum.GetValues(typeof(QuestComponentKind)))
        {
            var components = questTemplate.GetComponents(step);
            if (components.Length == 0)
                continue;

            for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
            {
                var component = components[componentIndex];
                var acts = QuestManager.Instance.GetActsInComponent(component.Id);

                if (acts.Count > 0)
                {
                    for (var actIndex = 0; actIndex < components.Length; actIndex++)
                    {
                        var act = acts[actIndex];
                        if (act.DetailType == "QuestActObjSphere")
                        {
                            if (acts[actIndex] is QuestActObjSphere actTemplate)
                                sphereIds.Add(actTemplate.SphereId.ToString());
                        }
                    }
                }
            }
        }

        return sphereIds;
    }

    public void PostLoad()
    {
        // Do nothing
    }

    public uint GetSphereIdFromQuest(uint questId)
    {
        var validQuestSpheres = _sphereQuests.Values.Where(x => x.QuestId == questId);
        
        foreach (var validQuestSphere in validQuestSpheres)
        {
            var res = _spheres.Values.FirstOrDefault(x => x.SphereDetailId == validQuestSphere.Id && x.SphereDetailType == "SphereQuest");
            if (res != null)
                return res.Id;
        }
        return 0;
    }

    public Spheres GetSphere(uint sphereId)
    {
        return _spheres.GetValueOrDefault(sphereId);
    }
    
    /// <summary>
    /// Checks if a position is inside the given SphereId
    /// </summary>
    /// <param name="sphereId">Sphere Id as defined in a Quest Act</param>
    /// <param name="value2">Unknown, always one except for skill 13305 (plant unidentified tree)</param>
    /// <returns>SphereQuest that was hit, null if none found</returns>
    public SphereQuest IsInsideAreaSphere(uint sphereId, uint value2, Vector3 worldPosition, uint requiredComponentId = 0)
    {
        if (!_spheres.TryGetValue(sphereId, out var dbSphere))
            return null;

        if (dbSphere.SphereDetailType != "SphereQuest")
            return null;

        if (!_sphereQuests.TryGetValue(dbSphere.SphereDetailId, out var dbSphereQuest))
            return null;

        var pakDataSpheres = SphereQuestManager.Instance.GetSpheresForQuest(dbSphereQuest.QuestId);
        foreach (var pakDataSphere in pakDataSpheres)
        {
            if (pakDataSphere.Contains(worldPosition) && (requiredComponentId == 0 || pakDataSphere.ComponentId == requiredComponentId))
                return pakDataSphere;
        }

        return null;
    }

}

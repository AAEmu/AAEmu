/*
 table 'ai_commands'
   2240	149	2	1	bridge  // FollowPath bridge
   2241	149	3	18397	0   // UseSkill id=18397
   2242	149	4	40	0       // Timeout 40 sec
   2243	149	2	2	bridge2 // FollowPath bridge2
 */

//public enum QuestNpcAiName -> quests\static\QuestNpcAiName
//{
//    None = 1,
//    FollowUnit = 2,
//    FollowPath = 3,
//    AttackUnit = 4,
//    GoAway = 5,
//    RunCommandSet = 6
//}

/* quest 291 'Rescue Bridget', component 4600, properties:
   npc_ai_id : 3 // QuestNpcAiName -> FollowPath
   npc_id : 3536 - Bridget
   ai_path_name : bridge2
   ai_path_type : 2 // PathType -> Remove

   team_share : t // квест засчитывается всем в группе! // the quest for everyone in the group!
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NLua;

namespace AAEmu.Game.Models.Game.AI.Params
{
    class HoldPositionAiParams
    {
        public int AlertDuration { get; set; }
        public bool AlertToAttack { get; set; }
        public int AlertSafeTargetRememberTime { get; set; }
        public List<uint> OnGroupLeaderDied { get; set; }
        public List<uint> OnGroupMemberDied { get; set; }

        public void Parse(string data)
        {
            using (var aiParams = new Lua())
            {
                aiParams.DoString($"data = {{\n{data}\n}}");

                AlertDuration = aiParams.GetInteger("data.alertDuration");
                AlertToAttack = (bool)(aiParams.GetObjectFromPath("data.alertToAttack") ?? false);
                AlertSafeTargetRememberTime = aiParams.GetInteger("data.alertSafeTargetRememberTime");

                OnGroupLeaderDied = new List<uint>();
                var onGroupLeaderDied = aiParams.GetTable("data.onGroupLeaderDied");
                if (onGroupLeaderDied != null)
                {
                    foreach (KeyValuePair<object, object> entry in onGroupLeaderDied)
                    {
                        OnGroupLeaderDied.Add((uint)entry.Value);
                    }
                }

                OnGroupMemberDied = new List<uint>();
                var onGroupMemberDied = aiParams.GetTable("data.onGroupLeaderDied");
                if (onGroupMemberDied != null)
                {
                    foreach (KeyValuePair<object, object> entry in onGroupMemberDied)
                    {
                        OnGroupMemberDied.Add((uint)entry.Value);
                    }
                }
            }
        }
    }
}

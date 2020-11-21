using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Models.Game.Skills;
using System.Collections.Generic;

namespace AAEmu.Game.Scripts.Commands
{
    class AddBuff : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addbuff", "usebuff", "add_buff", "use_buff", "buff" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "[\"AsTarget\"||\"View\"] [buffid]";
        }

        public string GetCommandHelpText()
        {
            return "Applies <buffid> to target with yourself as source.\r\n" +
                "If you preceed the buffid with the text \"AsTarget\" (or just \"t\") the source and target of the applied buff will be reversed.\r\n" +
                "You can also provide \"View\" to get a list of buffs applied to the target.";
        }

        public void Execute(Character character, string[] args)
        {
            int argsIdx = 0;
            BaseUnit sourceUnit = character;
            BaseUnit selectedUnit = character.CurrentTarget == null ? character : character.CurrentTarget;

            if ((args.Length <= 0) || (selectedUnit == null))
            {
                character.SendMessage("[AddBuff] " + CommandManager.CommandPrefix + "addbuff " + GetCommandLineHelp());
                return;
            }
            var a = args[0].ToLower();

            var targetUnit = selectedUnit;

            if ((a == "view") || (a == "v"))
            {
                var good = new List<Effect>();
                var bad = new List<Effect>();
                var hidden = new List<Effect>();
                targetUnit.Effects.GetAllBuffs(good, bad, hidden);
                if (good.Count + bad.Count + hidden.Count > 0)
                {
                    character.SendMessage("[Buff] Buffs on {0} - |cFFFFFFFF{1}|r", targetUnit.ObjId, targetUnit.Name);
                    foreach (var b in good)
                    {
                        var l = LocalizationManager.Instance.Get("buffs", "name", b.Template.BuffId);
                        character.SendMessage("[Buff] |cFF00FF00{0}|r - {1}", b.Template.BuffId, l);
                    }
                    foreach (var b in bad)
                    {
                        var l = LocalizationManager.Instance.Get("buffs", "name", b.Template.BuffId);
                        character.SendMessage("[Buff] |cFFFF0000{0}|r - {1}", b.Template.BuffId, l);
                    }
                    foreach (var b in hidden)
                    {
                        var l = LocalizationManager.Instance.Get("buffs", "name", b.Template.BuffId);
                        character.SendMessage("[Buff] |cFF6666FF{0}|r - {1}", b.Template.BuffId, l);
                    }
                }
                else
                {
                    character.SendMessage("[Buff] No buffs on unit {0} - |cFFFFFFFF{1}|r", targetUnit.ObjId, targetUnit.Name);
                }
                return;
            }

            if ((a == "astarget") || (a == "target") || (a == "at") || (a == "t"))
            {
                sourceUnit = selectedUnit;
                targetUnit = character;
                argsIdx++;
            }

            var casterObj = new SkillCasterUnit(sourceUnit.ObjId);
            var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            targetObj.ObjId = targetUnit.ObjId;

            if (int.TryParse(args[argsIdx], out var buffIdInt))
            {
                // TODO: add possibility to remove buffs using the negative of the buffid
                var buffId = (uint)Math.Abs(buffIdInt);
                var doRemove = buffIdInt < 0;

                var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
                if (buffTemplate == null)
                {
                    character.SendMessage("|cFFFF0000No such buffid {0} !|r",buffId);
                    return;
                }

                if (doRemove)
                    targetUnit.Effects.RemoveBuff(buffId);
                else
                    targetUnit.Effects.AddEffect(new Effect(targetUnit, character, casterObj, buffTemplate, null, System.DateTime.Now));
            }
            else
            {
                character.SendMessage("|cFFFF0000Parse error on buffid !|r");
                return;
            }
        }
    }
}

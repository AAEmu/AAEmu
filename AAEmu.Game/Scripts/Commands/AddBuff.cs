using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddBuff : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addbuff", "add_buff", "buff" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "\"View\" || [\"AsTarget\"] <buffId> <ab_level>";
        }

        public string GetCommandHelpText()
        {
            return "Adds or removes a buff to selected target. Negative BuffId's will remove a buff. " +
                "If precered by AsTarget (at) buff will be applied as if target was source. " +
                "If you provide view as a parameter, it will list the current buffs on target." +
                "You can also use 'v' instead of 'view' and 'at' or 't' instead of 'AsTarget'";
        }

        public void Execute(Character character, string[] args)
        {
            var firstArg = 0;
            if (args.Length <= 0)
            {
                character.SendMessage("[AddBuff] " + CommandManager.CommandPrefix + "addbuff " + GetCommandLineHelp());
                return;
            }

            Unit sourceUnit = character;
            Unit targetUnit = null ;

            if (!(character.CurrentTarget is Unit selectedUnit))
            {
                character.SendMessage("[AddBuff] |cFFFF0000No target unit selected|r");
                return;
            }
            targetUnit = selectedUnit;

            var a0 = args[0].ToLower();
            if ((a0 == "view") || (a0 == "v"))
            {
                var goodBuffs = new List<Effect>();
                var badBuffs = new List<Effect>();
                var hiddenBuffs = new List<Effect>();
                selectedUnit.Effects.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

                if (goodBuffs.Count + badBuffs.Count + hiddenBuffs.Count > 0)
                {
                    character.SendMessage("[AddBuff] Listing buffs for {0} - {1} !", selectedUnit.ObjId, selectedUnit.Name);
                    foreach (var b in goodBuffs)
                        character.SendMessage("[AddBuff] |cFF00FF00{0} - {1}|r", b.Template.Id, LocalizationManager.Instance.Get("buffs", "name", b.Template.Id));
                    foreach (var b in badBuffs)
                        character.SendMessage("[AddBuff] |cFFFF0000{0} - {1}|r", b.Template.Id, LocalizationManager.Instance.Get("buffs", "name", b.Template.Id));
                    foreach (var b in hiddenBuffs)
                        character.SendMessage("[AddBuff] |cFF6666FF{0} - {1}|r", b.Template.Id, LocalizationManager.Instance.Get("buffs", "name", b.Template.Id));
                }
                else
                {
                    character.SendMessage("[AddBuff] No buffs on {0} - {1}", selectedUnit.ObjId, selectedUnit.Name);
                }

                return;
            }

            if ((a0 == "astarget") || (a0 == "t") || (a0 == "at"))
            {
                firstArg++;
                sourceUnit = selectedUnit;
                targetUnit = character;
            }

            if (args.Length <= firstArg)
            {
                character.SendMessage("|cFFFF0000[AddBuff] No buffId provided ?|r");
                return;
            }

            if (!int.TryParse(args[firstArg], out var buffIdInt))
            {
                character.SendMessage("|cFFFF0000[AddBuff] Parse error buffId !|r");
                return;
            }

            var abLevel = 1u;
            if (args.Length > firstArg + 1)
                if (!uint.TryParse(args[firstArg + 1], out abLevel))
                    abLevel = 1u;

            uint buffId = (uint)Math.Abs(buffIdInt);

            var buffTemplate = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buffTemplate == null)
            {
                character.SendMessage("|cFFFF0000[AddBuff] Unknown buffId {0}|r", buffId);
                return;
            }

            if (buffIdInt > 0)
            {

                var casterObj = new SkillCasterUnit(sourceUnit.ObjId);
                var targetObj = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                targetObj.ObjId = targetUnit.ObjId;

                var newEffect = new Effect(targetUnit, sourceUnit, casterObj, buffTemplate, null, System.DateTime.Now)
                {
                    AbLevel = abLevel
                };
                targetUnit.Effects.AddEffect(newEffect);
            }
            if (buffIdInt < 0)
            {
                if (selectedUnit.Effects.CheckBuff(buffId))
                    selectedUnit.Effects.RemoveBuff(buffId);
                else
                {
                    character.SendMessage("|cFFFF0000[AddBuff] Target didn't have buff to remove|r", buffId);
                }
            }
            else
            {
                // I think this might be zero
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Shipyard
{
    public sealed class Shipyard : Unit
    {
        public override float Scale => 1.0f;
        private object _lock = new object();
        private bool _isDirty;
        private int _allAction;
        private int _baseAction;
        private int _numAction;
        private int _currentStep;
        private ShipyardsTemplate _template;

        public override UnitTypeFlag TypeFlag { get; } = UnitTypeFlag.Shipyard;
        public override UnitCustomModelParams ModelParams { get; set; }
        public ShipyardData ShipyardData { get; set; }
        public ShipyardsTemplate Template
        {
            get => _template;
            set
            {
                _template = value;
                _allAction = _template.ShipyardSteps.Values.Sum(step => step.NumActions);
            }
        }
        private bool IsDirty { get => _isDirty; set => _isDirty = value; }
        public int AllAction { get => _allAction; set { _allAction = value; _isDirty = true; } }
        public int BaseAction {
            get => _baseAction;
            private set
            {
                _baseAction = value; _isDirty = true;
            }
        }
        public int CurrentAction => BaseAction + NumAction;
        public int NumAction {
            get => _numAction;
            private set
            {
                _numAction = value; _isDirty = true;
            }
        }
        public int CurrentStep
        {
            get => _currentStep;
            private set
            {
                _currentStep = value;
                _isDirty = true;
                ModelId = _currentStep == -1 ? Template.MainModelId : Template.ShipyardSteps[_currentStep].ModelId;
                if (_currentStep <= 0) { return; }

                BaseAction = 0;
                for (var i = 0; i < _currentStep; i++)
                    BaseAction += Template.ShipyardSteps[i].NumActions;
            }
        }

        public Shipyard()
        {
            ModelParams = new UnitCustomModelParams();
            IsDirty = true;
            Events.OnDeath += OnDeath;
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCShipyardStatePacket(ShipyardData));

            base.AddVisibleObject(character);
        }

        public override void RemoveVisibleObject(Character character)
        {
            base.RemoveVisibleObject(character);

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }

        #region Attributes

        public int Str
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.Str);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var result = formula.Evaluate(parameters);
                var res = (int)result;
                foreach (var bonus in GetBonuses(UnitAttribute.Str))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Dex
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.Dex);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Dex))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Sta
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.Sta);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Sta))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Int
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.Int);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Int))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Spi
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.Spi);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Spi))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public int Fai
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.Fai);
                var parameters = new Dictionary<string, double> { ["level"] = Level };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.Fai))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int MaxHp
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.MaxHealth);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxHealth))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int HpRegen
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.HealthRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.HealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int PersistentHpRegen
        {
            get
            {
                var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard,
                    UnitFormulaKind.PersistentHealthRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentHealthRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int MaxMp
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.MaxMana);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                foreach (var bonus in GetBonuses(UnitAttribute.MaxMana))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int MpRegen
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard, UnitFormulaKind.ManaRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res += Spi / 10;
                foreach (var bonus in GetBonuses(UnitAttribute.ManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        public override int PersistentMpRegen
        {
            get
            {
                var formula =
                    FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Shipyard,
                        UnitFormulaKind.PersistentManaRegen);
                var parameters = new Dictionary<string, double>
                {
                    ["level"] = Level,
                    ["str"] = Str,
                    ["dex"] = Dex,
                    ["sta"] = Sta,
                    ["int"] = Int,
                    ["spi"] = Spi,
                    ["fai"] = Fai
                };
                var res = (int)formula.Evaluate(parameters);
                res /= 5; // TODO ...
                foreach (var bonus in GetBonuses(UnitAttribute.PersistentManaRegen))
                {
                    if (bonus.Template.ModifierType == UnitModifierType.Percent)
                    {
                        res += (int)(res * bonus.Value / 100f);
                    }
                    else
                    {
                        res += bonus.Value;
                    }
                }

                return res;
            }
        }

        #endregion

        private void OnDeath(object sender, EventArgs args)
        {
            _log.Debug("Shipyard died ObjId:{0} - TemplateId:{1} - {2}", ObjId, ShipyardData.TemplateId, ShipyardData.OwnerName);
            ShipyardManager.Instance.RemoveShipyard(this);
        }

        public void AddBuildAction()
        {
            if (CurrentStep == -1)
                return;

            lock (_lock)
            {
                var nextAction = NumAction + 1;
                if (Template.ShipyardSteps[CurrentStep].NumActions > nextAction)
                    NumAction = nextAction;
                else
                {
                    NumAction = 0;
                    var nextStep = CurrentStep + 1;
                    if (Template.ShipyardSteps.Count > nextStep)
                        CurrentStep = nextStep;
                    else
                    {
                        CurrentStep = -1;
                    }
                }
            }
        }
    }
}

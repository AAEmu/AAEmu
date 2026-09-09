## Summary

Adds minimal null guards for missing skill and buff templates before runtime casts/effects dereference them. Invalid template references now return, continue, or cancel only the affected cast path instead of crashing AI/server execution.

## Per File

- `AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs`: Returns the existing `InvalidSkill` result when idle self-skill or combat skill templates are missing.
- `AAEmu.Game/Models/Game/AI/v2/Behaviors/Common/SpawningBehavior.cs`: Skips missing spawn-skill templates and continues the spawn-skill loop.
- `AAEmu.Game/Models/Game/AI/v2/Behaviors/WildBoar/WildBoarAttackBehavior.cs`: Aborts the start-combat skill when its template is missing and skips missing spurt-skill templates.
- `AAEmu.Game/Models/Game/Units/Unit.cs`: Returns `SkillResult.InvalidSkill` for unit and doodad casts with missing templates.
- `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/SkillUse.cs`: Returns before scheduling a triggered skill when the referenced template is missing.
- `AAEmu.Game/Models/Game/DoodadObj/Funcs/DoodadFuncSkillHit.cs`: Returns before fake item/doodad skill-hit processing when the referenced template is missing.
- `AAEmu.Game/Models/Game/DoodadObj/Funcs/DoodadFuncFakeUse.cs`: Returns before fake doodad skill use when the referenced template is missing, leaving phase advancement only for legitimate paths.
- `AAEmu.Game/Models/Game/Units/Route/Simulation.cs`: Skips missing patrol skill casts, clears `SkillId`, and continues patrol movement with the default delay.
- `AAEmu.Game/Models/Game/Gimmicks/Gimmick.cs`: Checks the skill template before marking `SkillStarted` or scheduling the gimmick skill.
- `AAEmu.Game/Core/Packets/C2G/CSStartSkillPacket.cs`: Fetches one `requestedSkillTemplate` once, returns when missing, and reuses it in the active mount/slave, item, learned, variant, undefined, and common/default branches.
- `AAEmu.Game/Models/Game/Skills/Effects/BuffEffect.cs`: Returns at the start of `Apply` when the loader left `Buff` null.
- `AAEmu.Game/Models/Game/Char/CharacterCraft.cs`: Cancels crafting when a craft references a missing skill template, including repeat scheduling.

## Safety Grep

`rg -n "new Skill\\([^;\\n]*GetSkillTemplate\\(" AAEmu.Game -g '*.cs'` leaves only commented legacy examples in `Combat.cs` and `Gimmick.cs`.

## Changed Files

- `AAEmu.Game/Core/Packets/C2G/CSStartSkillPacket.cs`
- `AAEmu.Game/Models/Game/AI/v2/Behaviors/Common/SpawningBehavior.cs`
- `AAEmu.Game/Models/Game/AI/v2/Behaviors/WildBoar/WildBoarAttackBehavior.cs`
- `AAEmu.Game/Models/Game/AI/v2/Framework/Behavior.cs`
- `AAEmu.Game/Models/Game/Char/CharacterCraft.cs`
- `AAEmu.Game/Models/Game/DoodadObj/Funcs/DoodadFuncFakeUse.cs`
- `AAEmu.Game/Models/Game/DoodadObj/Funcs/DoodadFuncSkillHit.cs`
- `AAEmu.Game/Models/Game/Gimmicks/Gimmick.cs`
- `AAEmu.Game/Models/Game/Skills/Effects/BuffEffect.cs`
- `AAEmu.Game/Models/Game/Skills/Effects/SpecialEffects/SkillUse.cs`
- `AAEmu.Game/Models/Game/Units/Route/Simulation.cs`
- `AAEmu.Game/Models/Game/Units/Unit.cs`

## Tests

Not run by request.

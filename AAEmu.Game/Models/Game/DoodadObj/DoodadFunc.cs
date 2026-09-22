using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj;

public class DoodadFunc
{
    public uint GroupId { get; set; }
    public uint FuncId { get; set; }
    public uint FuncKey { get; set; }
    public string FuncType { get; set; }
    public int NextPhase { get; set; }
    public uint SoundId { get; set; }
    public uint SkillId { get; set; }
    public uint PermId { get; set; }
    public int Count { get; set; }

    //This acts as an interface/relay for doodad function chain
    //public async void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    public void Use(BaseUnit caster, Doodad owner, uint skillId = 0, int nextPhase = 0)
    {
        var template = DoodadManager.Instance.GetFuncTemplate(FuncId, FuncType);
        // Helm attachments store occupy skill on doodad_funcs.func_skill_id; F-use often arrives with skillId 0.
        var appliedSkill = skillId != 0 ? skillId : SkillId;
        template?.Use(caster, owner, appliedSkill, nextPhase);

        // Some FakeUse rows are gated by doodad_funcs.func_skill_id rather than the template's
        // fake_skill_id. Only advance after this row's declared skill was dispatched; accepting any
        // positive skill here lets unrelated interactions trigger the func.
        if (caster != null &&
            template is DoodadFuncFakeUse &&
            SkillId > 0 &&
            appliedSkill == SkillId &&
            nextPhase > 0)
        {
            owner.ToNextPhase = true;
        }
    }
}

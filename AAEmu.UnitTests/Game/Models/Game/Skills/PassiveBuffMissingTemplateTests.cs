using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// A passive_buffs row the loader dropped (its buff_id has no <c>buffs</c> row) has to be inert at every
/// call site rather than dereferenced. 10.0.2.13 ships four such rows (51 → 581, 268 → 11161,
/// 274 → 13819, 289 → 15562), and the ids arrive from a saved character and from CSLearnBuffPacket, so
/// removing the rows alone only moved the crash to <c>CharacterSkills.AddBuff</c>, the character load and
/// <c>PassiveBuff.Apply</c>.
/// </summary>
[NotInParallel]
public class PassiveBuffMissingTemplateTests
{
    private const uint DroppedPassiveId = 51;  // passive_buffs 51 names buff 581, which has no buffs row
    private const uint DroppedBuffId = 581;

    private FieldInfo _skillManagerField;
    private object _previousSkillManager;

    [Before(Test)]
    public void InstallSkillManager()
    {
        _skillManagerField = typeof(Singleton<SkillManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousSkillManager = _skillManagerField.GetValue(null);
        // Empty tables: every lookup answers null, which is the shape the four dropped rows leave behind.
        _skillManagerField.SetValue(null,
            new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object));
    }

    [After(Test)]
    public void RestoreSkillManager() => _skillManagerField.SetValue(null, _previousSkillManager);

    [Test]
    public async Task Apply_NullTemplate_IsInert()
    {
        var owner = new Unit { ObjId = 1 };

        Exception error = null;
        try
        {
            new PassiveBuff { Id = DroppedPassiveId, Template = null }.Apply(owner);
        }
        catch (Exception e)
        {
            error = e;
        }

        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task Apply_TemplateWithoutABuffRow_IsInert()
    {
        var owner = new Unit { ObjId = 1 };
        var passive = new PassiveBuff
        {
            Id = DroppedPassiveId,
            Template = new PassiveBuffTemplate { Id = DroppedPassiveId, BuffId = DroppedBuffId }
        };

        Exception error = null;
        try
        {
            passive.Apply(owner);
        }
        catch (Exception e)
        {
            error = e;
        }

        await Assert.That(error).IsNull();
        await Assert.That(owner.Buffs.CheckBuff(DroppedBuffId)).IsFalse();
    }

    [Test]
    public async Task CharacterSkillsAddBuff_UnknownPassiveId_IsRefused()
    {
        // The id is client-supplied on CSLearnBuffPacket; the template lookup returns null for it.
        var character = new Character(new UnitCustomModelParams()) { Id = 1, ObjId = 1, Level = 50 };
        // Character.Skills is built by Character.Load (Character.cs:3908), so a fresh one has to make it.
        character.Skills = new CharacterSkills(character);

        Exception error = null;
        try
        {
            character.Skills.AddBuff(DroppedPassiveId);
        }
        catch (Exception e)
        {
            error = e;
        }

        await Assert.That(error).IsNull();
        await Assert.That(character.Skills.PassiveBuffs).IsEmpty();
    }
}

using AAEmu.Game.Models.Game.Mails;
using NLua;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class MailForTaxTests
{
    [Test]
    [Arguments("Miner's Farmhouse")]
    [Arguments("Casa de José")]
    [Arguments("Farmer's \\ 'cottage' \"north\"")]
    [Arguments("line\r\nnext\t\0" + "123")]
    [Arguments("'); injected = true; --")]
    [Arguments("C1 \u0085 \u009f and DEL \u007f123")]
    [Arguments("")]
    public async Task HouseNameRemainsOneLiteralLuaArgument(string name)
    {
        using var lua = new Lua();
        lua.State.Encoding = System.Text.Encoding.UTF8;
        lua.DoString("function body(...) return select('#', ...), ... end");
        var result = lua.DoString($"return body('{MailForTax.EscapeLuaString(name)}', '1789166223', '750000')");
        await Assert.That(Convert.ToInt32(result[0])).IsEqualTo(3);
        await Assert.That((string)result[1]).IsEqualTo(name);
        await Assert.That((string)result[2]).IsEqualTo("1789166223");
        await Assert.That((string)result[3]).IsEqualTo("750000");
        await Assert.That(lua["injected"]).IsNull();
    }
}

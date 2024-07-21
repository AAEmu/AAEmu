namespace AAEmu.UnitTests.Utils;

using System;
using System.IO;
using AAEmu.Game.IO;
using Microsoft.Extensions.Configuration;

internal class ConfigurationUtils
{
    internal static string GetValidGamePackPath()
    {
        var clientData = new ConfigurationBuilder()
            .AddUserSecrets<ConfigurationUtils>()
            .Build()
            .GetSection("ClientData:ClientData")
            .Get<ClientDataConfig>();

        if (clientData is null)
        {
            throw new InvalidOperationException("ClientData is not configured");
        }

        foreach (var source in clientData.Sources)
        {
            if (source.EndsWith("game_pak") && File.Exists(source))
            {
                return source;
            }
        }

        throw new FileNotFoundException("game_pak file not found");
    }
}

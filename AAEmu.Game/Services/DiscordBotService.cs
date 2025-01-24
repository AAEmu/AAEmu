using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;

namespace AAEmu.Game.Services;

public class DiscordBotService : IHostedService
{
    private DiscordSocketClient _client;
    private int playerCount = 0;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = AppConfiguration.Instance.DiscordToken;
        if (string.IsNullOrEmpty(token))
            return;

        _client = new DiscordSocketClient();

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _client.MessageReceived += MessageReceived;

        await UpdatePlayerCountPeriodically(TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(-1, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client == null)
            return;
        await _client.StopAsync();
    }

    private async Task UpdatePlayerCountPeriodically(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (true)
        {
            playerCount = WorldManager.Instance.GetAllCharacters().Count();
            await _client.SetActivityAsync(new Discord.Game("with " + playerCount + " other players"));
            await Task.Delay(interval, cancellationToken);
        }
    }

    public static async Task MessageReceived(SocketMessage message)
    {
        if (message.Content == "/online")
        {
            var playerNames = WorldManager.Instance.GetAllCharacters().Select(c => c.Name).ToList();
            if (playerNames.Count > 0)
                await message.Channel.SendMessageAsync("Players online: " + string.Join(", ", playerNames));
            else
                await message.Channel.SendMessageAsync("No players online.");
        }
    }
}

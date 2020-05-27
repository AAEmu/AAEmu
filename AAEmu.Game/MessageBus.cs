using System;
using AAEmu.Commons.Configuration.Server;
using AAEmu.Commons.Messages;
using AAEmu.Game.Bus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SlimMessageBus;
using SlimMessageBus.Host.AspNetCore;
using SlimMessageBus.Host.Config;
using SlimMessageBus.Host.Hybrid;
using SlimMessageBus.Host.Memory;
using SlimMessageBus.Host.Redis;
using SlimMessageBus.Host.Serialization.Json;

namespace AAEmu.Game
{
    public static class MessageBus
    {
        public static IMessageBus Build(IServiceProvider provider)
        {
            var configuration = provider.GetService<IConfiguration>().Get<GameServerConfiguration>();

            var hybridBusSettings = new HybridMessageBusSettings
            {
                ["Memory"] = builder =>
                {
                    builder
                        .Do(x => DoMemory(x, configuration))
                        .WithProviderMemory(new MemoryMessageBusSettings {EnableMessageSerialization = false});
                },
                ["Internal"] = builder =>
                {
                    builder
                        .Do(x => DoInternal(x, configuration))
                        .WithProviderRedis(
                            new RedisMessageBusSettings(configuration.ConnectionStrings.RedisConnection));
                }
            };

            var result = MessageBusBuilder.Create()
                .WithDependencyResolver(new AspNetCoreMessageBusDependencyResolver(provider, null))
                .WithSerializer(new JsonMessageSerializer())
                .WithProviderHybrid(hybridBusSettings);

            return result.Build();
        }

        /**
         * Settings local events
         */
        private static void DoMemory(MessageBusBuilder builder, GameServerConfiguration configuration)
        {
            // TODO : local events
        }

        /**
         * Settings internal events
         */
        private static void DoInternal(MessageBusBuilder builder, GameServerConfiguration configuration)
        {
            var instance = "instance-" + configuration.Id;
            var replyToTopic = "game-" + instance + "-response";

            builder
                .Produce<PlayerReconnectRequest>(x => x.DefaultTopic(x.Settings.MessageType.Name))
                .Handle<RequestInfoRequest, RequestInfoResponse>(x =>
                    x.Topic(x.MessageType.Name)
                        .WithHandler<RequestInfoRequestHandler>()
                        .Instances(1)
                )
                .Handle<PlayerEnterRequest, PlayerEnterResponse>(x =>
                    x.Topic(x.MessageType.Name)
                        .WithHandler<PlayerEnterRequestHandler>()
                        .Instances(1)
                )
                .ExpectRequestResponses(x =>
                {
                    x.ReplyToTopic(replyToTopic);
                    x.DefaultTimeout(TimeSpan.FromSeconds(60));
                });
        }
    }
}

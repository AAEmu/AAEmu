using System;
using AAEmu.Commons.Configuration.Server;
using AAEmu.Commons.Messages;
using AAEmu.Login.Bus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SlimMessageBus;
using SlimMessageBus.Host.AspNetCore;
using SlimMessageBus.Host.Config;
using SlimMessageBus.Host.Redis;
using SlimMessageBus.Host.Serialization.Json;

namespace AAEmu.Login
{
    public static class MessageBus
    {
        public static IMessageBus Build(IServiceProvider provider)
        {
            var configuration = provider.GetService<IConfiguration>().Get<AuthServerConfiguration>();

            var result = MessageBusBuilder
                .Create()
                .WithSerializer(new JsonMessageSerializer())
                .WithDependencyResolver(new AspNetCoreMessageBusDependencyResolver(provider, null))
                .WithProviderRedis(new RedisMessageBusSettings(configuration.ConnectionStrings.RedisConnection))
                .Do(builder => Do(builder, configuration));

            return result.Build();
        }

        private static void Do(MessageBusBuilder builder, AuthServerConfiguration configuration)
        {
            builder
                .Produce<RequestInfoRequest>(x =>
                {
                    x.DefaultTopic(x.Settings.MessageType.Name)
                        .DefaultTimeout(TimeSpan.FromSeconds(10));
                })
                .Produce<PlayerEnterRequest>(x => x.DefaultTopic(x.Settings.MessageType.Name))
                .Handle<PlayerReconnectRequest, PlayerReconnectResponse>(x =>
                    x.Topic(x.MessageType.Name)
                        .WithHandler<PlayerReconnectRequestHandler>()
                )
                .ExpectRequestResponses(x =>
                {
                    x.ReplyToTopic("login-instance-response");
                    x.DefaultTimeout(TimeSpan.FromSeconds(60));
                });
        }
    }
}

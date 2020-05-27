using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AAEmu.Commons.Network.Core.Messages;
using AAEmu.Commons.Network.Core.Packet;
using Microsoft.Extensions.Logging;

namespace AAEmu.Commons.Network.Core
{
    public interface
        IClientGameProtocolHandler<TOpcode, TMessageAttribute, TMessageHandlerAttribute> : IProtocolHandler
        where TOpcode : Enum
        where TMessageAttribute : Attribute, IMessageAttribute<TOpcode>
        where TMessageHandlerAttribute : Attribute, IMessageHandlerAttribute<TOpcode>
    {
        IReadable GetMessage(TOpcode opcode);
        bool GetOpcode(IWritable message, out TOpcode opcode);
        bool GetLevel(IWritable message, out byte level);
        Type GetMessageHandler(TOpcode opcode);
    }

    public class ClientGameProtocolHandler<TOpcode, TMessageAttribute, TMessageHandlerAttribute> : ProtocolHandler,
        IClientGameProtocolHandler<TOpcode, TMessageAttribute, TMessageHandlerAttribute>
        where TOpcode : Enum
        where TMessageAttribute : Attribute, IMessageAttribute<TOpcode>
        where TMessageHandlerAttribute : Attribute, IMessageHandlerAttribute<TOpcode>
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<ClientGameProtocolHandler<TOpcode, TMessageAttribute, TMessageHandlerAttribute>> _logger;

        private delegate IReadable MessageFactoryDelegate();

        private ImmutableDictionary<TOpcode, MessageFactoryDelegate> _clientMessageFactories;
        private ImmutableDictionary<Type, TOpcode> _serverMessageOpcodes;
        private ImmutableDictionary<Type, byte> _serverMessageLevels;

        private ImmutableDictionary<TOpcode, Type> _clientMessageHandlers;

        public ClientGameProtocolHandler(
            IServiceProvider provider,
            ILogger<ClientGameProtocolHandler<TOpcode, TMessageAttribute, TMessageHandlerAttribute>> logger)
        {
            _provider = provider;
            _logger = logger;

            InitialiseMessages();
            InitialiseMessageHandlers();
        }

        public override void OnReceived(Session session, byte[] buffer, long offset, long size)
        {
            try
            {
                var stream = new PacketStream();
                // if (LastPacket != null)
                // {
                //     stream.Insert(0, LastPacket);
                //     LastPacket = null;
                // }

                stream.Insert(stream.Count, buffer, (int)offset, (int)size);
                while (stream != null && stream.Count > 0)
                {
                    ushort len;
                    try
                    {
                        len = stream.ReadUInt16();
                    }
                    catch (MarshalException)
                    {
                        stream.Rollback();
                        // LastPacket = stream;
                        stream = null;
                        continue;
                    }

                    var packetLen = len + stream.Pos;
                    if (packetLen <= stream.Count)
                    {
                        stream.Rollback();
                        var stream2 = new PacketStream();
                        stream2.Replace(stream, 0, packetLen);
                        if (stream.Count > packetLen)
                        {
                            var stream3 = new PacketStream();
                            stream3.Replace(stream, packetLen, stream.Count - packetLen);
                            stream = stream3;
                        }
                        else
                            stream = null;

                        stream2.ReadUInt16(); //len
                        var type = stream2.ReadUInt16();
                        var opcode = (TOpcode)(object)type;
                        var packet = new ClientGamePacket<TOpcode>(opcode, stream2);
                        HandlePacket(session, packet);
                    }
                    else
                    {
                        stream.Rollback();
                        // LastPacket = stream;
                        stream = null;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "");
            }
        }

        public override void OnSend(Session session, IWritable message)
        {
            if (!GetOpcode(message, out var opcode))
            {
                _logger.LogWarning("Failed to get message opcode");
                return;
            }

            var packet = new ServerGamePacket<TOpcode>(opcode, message);
            OnSend(session, packet);
        }

        public virtual void OnSend(Session session, ServerGamePacket<TOpcode> packet)
        {
            var stream = new PacketStream();
            stream.Write(new PacketStream().Write((ushort)(object)packet.Opcode).Write(packet.Data, false));

            session.SendAsync(stream);
        }

        public virtual void HandlePacket(Session session, ClientGamePacket<TOpcode> packet)
        {
            var message = GetMessage(packet.Opcode);
            if (message == null)
            {
                _logger.LogWarning(
                    $"Unknown client packet received. Opcode : {typeof(TOpcode).Name}:{packet.Opcode:X}. Data : {packet.Data}");
                return;
            }

            var handlerType = GetMessageHandler(packet.Opcode);
            if (handlerType == null)
            {
                _logger.LogWarning($"Unhandled packet received. Opcode : {typeof(TOpcode).Name}:{packet.Opcode:X}");
                return;
            }

            var handler = _provider.GetService(handlerType);
            if (handler == null)
            {
                _logger.LogWarning($"Create packet handler failed. Opcode : {typeof(TOpcode).Name}:{packet.Opcode:X}");
                return;
            }

            message.Read(packet.Data);

            try
            {
                ((IHandler)handler).Handler(session, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "");
            }
        }

        public IReadable GetMessage(TOpcode opcode)
        {
            return _clientMessageFactories.TryGetValue(opcode, out var factory)
                ? factory.Invoke()
                : null;
        }

        public bool GetOpcode(IWritable message, out TOpcode opcode)
        {
            return _serverMessageOpcodes.TryGetValue(message.GetType(), out opcode);
        }

        public bool GetLevel(IWritable message, out byte level)
        {
            return _serverMessageLevels.TryGetValue(message.GetType(), out level);
        }

        public Type GetMessageHandler(TOpcode opcode)
        {
            return _clientMessageHandlers.TryGetValue(opcode, out var handler)
                ? handler
                : null;
        }

        private void InitialiseMessages()
        {
            var messageFactories = new Dictionary<TOpcode, MessageFactoryDelegate>();
            var messageOpcodes = new Dictionary<Type, TOpcode>();
            var messageLevels = new Dictionary<Type, byte>();

            foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                .Concat(Assembly.GetEntryAssembly()?.GetTypes() ?? throw new InvalidOperationException()))
            {
                var attribute = type.GetCustomAttribute<TMessageAttribute>();
                if (attribute == null)
                    continue;

                if (typeof(IReadable).IsAssignableFrom(type))
                {
                    var @new = Expression.New(type.GetConstructor(Type.EmptyTypes) ??
                                              throw new InvalidOperationException());
                    messageFactories.Add(attribute.Opcode, Expression.Lambda<MessageFactoryDelegate>(@new).Compile());
                }

                if (typeof(IWritable).IsAssignableFrom(type))
                {
                    messageOpcodes.Add(type, attribute.Opcode);
                    messageLevels.Add(type, attribute.Level);
                }
            }

            _clientMessageFactories = messageFactories.ToImmutableDictionary();
            _serverMessageOpcodes = messageOpcodes.ToImmutableDictionary();
            _serverMessageLevels = messageLevels.ToImmutableDictionary();
            _logger.LogInformation(
                $"Initialised {_clientMessageFactories.Count} client message {(_clientMessageFactories.Count == 1 ? "factory" : "factories")}.");
            _logger.LogInformation($"Initialised {_serverMessageOpcodes.Count} server message(s).");
        }

        private void InitialiseMessageHandlers()
        {
            var messageHandlers = new Dictionary<TOpcode, Type>();

            foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                .Concat(Assembly.GetEntryAssembly()?.GetTypes() ?? throw new InvalidOperationException()))
            {
                var attribute = type.GetCustomAttribute<TMessageHandlerAttribute>();
                if (attribute == null)
                    continue;

                if (typeof(IHandler).IsAssignableFrom(type))
                    messageHandlers.Add(attribute.Opcode, type);
            }

            _clientMessageHandlers = messageHandlers.ToImmutableDictionary();
            _logger.LogInformation($"Initialised {_clientMessageHandlers.Count} message handler(s).");
        }
    }
}

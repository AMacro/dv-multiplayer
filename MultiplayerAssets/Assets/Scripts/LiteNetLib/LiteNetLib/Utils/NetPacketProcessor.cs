using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteNetLib.Utils
{
    public class NetPacketProcessor
    {
        private static IReadOnlyDictionary<Type, byte> PacketIdDict;

        public static byte PacketCount => (byte)(PacketIdDict?.Count ?? 0);
        public static byte CurrentlyProcessingPacket { get; private set; }

        public static IReadOnlyDictionary<Type, byte> RegisterPacketTypes()
        {
            Type[] packetTypes = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.Namespace?.StartsWith("Multiplayer.Networking.Packets") == true || (t.Namespace == "LiteNetLib" && t.Name.EndsWith("Packet")))
                .OrderBy(t => t.FullName)
                .ToArray();

            if (packetTypes.Length > byte.MaxValue)
                throw new OverflowException($"There's more than {byte.MaxValue} packet types!");

            PacketIdDict = packetTypes
                .Select((t, i) => new { Key = t, Value = (byte)i })
                .ToDictionary(x => x.Key, x => x.Value);

            return PacketIdDict;
        }

        protected delegate void SubscribeDelegate(NetDataReader reader, object userData);

        private readonly NetManager _netManager;
        private readonly NetSerializer _netSerializer;
        private readonly Dictionary<byte, SubscribeDelegate> _callbacks = new Dictionary<byte, SubscribeDelegate>();

        public NetPacketProcessor(NetManager netManager)
        {
            _netManager = netManager;
            _netSerializer = new NetSerializer();
        }

        public NetPacketProcessor(NetManager netManager, int maxStringLength)
        {
            _netManager = netManager;
            _netSerializer = new NetSerializer(maxStringLength);
        }

        private static byte GetId<T>()
        {
            if (PacketIdDict.TryGetValue(typeof(T), out byte id))
                return id;
            throw new ArgumentException($"Failed to find packet ID for {typeof(T)}");
        }

        protected SubscribeDelegate GetCallbackFromData(NetDataReader reader)
        {
            byte id = reader.GetByte();
            CurrentlyProcessingPacket = id;
            if (!_callbacks.TryGetValue(id, out var action))
            {
                throw new ParseException($"Undefined packet {id} in NetDataReader");
            }
            return action;
        }

        private static void WriteId<T>(NetDataWriter writer)
        {
            writer.Put(GetId<T>());
        }

        /// <summary>
        /// Register nested property type
        /// </summary>
        /// <typeparam name="T">INetSerializable structure</typeparam>
        public void RegisterNestedType<T>() where T : struct, INetSerializable
        {
            _netSerializer.RegisterNestedType<T>();
        }

        /// <summary>
        /// Register nested property type
        /// </summary>
        /// <param name="writeDelegate"></param>
        /// <param name="readDelegate"></param>
        public void RegisterNestedType<T>(Action<NetDataWriter, T> writeDelegate, Func<NetDataReader, T> readDelegate)
        {
            _netSerializer.RegisterNestedType<T>(writeDelegate, readDelegate);
        }

        /// <summary>
        /// Register nested property type
        /// </summary>
        /// <typeparam name="T">INetSerializable class</typeparam>
        public void RegisterNestedType<T>(Func<T> constructor) where T : class, INetSerializable
        {
            _netSerializer.RegisterNestedType(constructor);
        }

        /// <summary>
        /// Reads all available data from NetDataReader and calls OnReceive delegates
        /// </summary>
        /// <param name="reader">NetDataReader with packets data</param>
        public void ReadAllPackets(NetDataReader reader)
        {
            while (reader.AvailableBytes > 0)
                ReadPacket(reader);
        }

        /// <summary>
        /// Reads all available data from NetDataReader and calls OnReceive delegates
        /// </summary>
        /// <param name="reader">NetDataReader with packets data</param>
        /// <param name="userData">Argument that passed to OnReceivedEvent</param>
        /// <exception cref="ParseException">Malformed packet</exception>
        public void ReadAllPackets(NetDataReader reader, object userData)
        {
            while (reader.AvailableBytes > 0)
                ReadPacket(reader, userData);
        }

        /// <summary>
        /// Reads one packet from NetDataReader and calls OnReceive delegate
        /// </summary>
        /// <param name="reader">NetDataReader with packet</param>
        /// <exception cref="ParseException">Malformed packet</exception>
        public void ReadPacket(NetDataReader reader)
        {
            ReadPacket(reader, null);
        }

        public void Write<T>(NetDataWriter writer, T packet) where T : class, new()
        {
            WriteId<T>(writer);
            _netSerializer.Serialize(writer, packet);
            if (!_netManager.EnableStatistics)
                return;
            _netManager.Statistics.IncrementPacketsWritten(GetId<T>());
            _netManager.Statistics.AddBytesWritten(GetId<T>(), writer.Length);
        }

        public void WriteNetSerializable<T>(NetDataWriter writer, ref T packet) where T : INetSerializable
        {
            WriteId<T>(writer);
            packet.Serialize(writer);
            if (!_netManager.EnableStatistics)
                return;
            _netManager.Statistics.IncrementPacketsWritten(GetId<T>());
            _netManager.Statistics.AddBytesWritten(GetId<T>(), writer.Length);
        }

        /// <summary>
        /// Reads one packet from NetDataReader and calls OnReceive delegate
        /// </summary>
        /// <param name="reader">NetDataReader with packet</param>
        /// <param name="userData">Argument that passed to OnReceivedEvent</param>
        /// <exception cref="ParseException">Malformed packet</exception>
        public void ReadPacket(NetDataReader reader, object userData)
        {
            GetCallbackFromData(reader)(reader, userData);
        }

        /// <summary>
        /// Register and subscribe to packet receive event
        /// </summary>
        /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
        /// <param name="packetConstructor">Method that constructs packet instead of slow Activator.CreateInstance</param>
        /// <exception cref="InvalidTypeException"><typeparamref name="T"/>'s fields are not supported, or it has no fields</exception>
        public void Subscribe<T>(Action<T> onReceive, Func<T> packetConstructor) where T : class, new()
        {
            _netSerializer.Register<T>();
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                var reference = packetConstructor();
                _netSerializer.Deserialize(reader, reference);
                onReceive(reference);
            };
        }

        /// <summary>
        /// Register and subscribe to packet receive event (with userData)
        /// </summary>
        /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
        /// <param name="packetConstructor">Method that constructs packet instead of slow Activator.CreateInstance</param>
        /// <exception cref="InvalidTypeException"><typeparamref name="T"/>'s fields are not supported, or it has no fields</exception>
        public void Subscribe<T, TUserData>(Action<T, TUserData> onReceive, Func<T> packetConstructor) where T : class, new()
        {
            _netSerializer.Register<T>();
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                var reference = packetConstructor();
                _netSerializer.Deserialize(reader, reference);
                onReceive(reference, (TUserData)userData);
            };
        }

        /// <summary>
        /// Register and subscribe to packet receive event
        /// This method will overwrite last received packet class on receive (less garbage)
        /// </summary>
        /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
        /// <exception cref="InvalidTypeException"><typeparamref name="T"/>'s fields are not supported, or it has no fields</exception>
        public void SubscribeReusable<T>(Action<T> onReceive) where T : class, new()
        {
            _netSerializer.Register<T>();
            var reference = new T();
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                _netSerializer.Deserialize(reader, reference);
                onReceive(reference);
            };
        }

        /// <summary>
        /// Register and subscribe to packet receive event
        /// This method will overwrite last received packet class on receive (less garbage)
        /// </summary>
        /// <param name="onReceive">event that will be called when packet deserialized with ReadPacket method</param>
        /// <exception cref="InvalidTypeException"><typeparamref name="T"/>'s fields are not supported, or it has no fields</exception>
        public void SubscribeReusable<T, TUserData>(Action<T, TUserData> onReceive) where T : class, new()
        {
            _netSerializer.Register<T>();
            var reference = new T();
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                _netSerializer.Deserialize(reader, reference);
                onReceive(reference, (TUserData)userData);
            };
        }

        public void SubscribeNetSerializable<T, TUserData>(
            Action<T, TUserData> onReceive,
            Func<T> packetConstructor) where T : INetSerializable
        {
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                var pkt = packetConstructor();
                pkt.Deserialize(reader);
                onReceive(pkt, (TUserData)userData);
            };
        }

        public void SubscribeNetSerializable<T>(
            Action<T> onReceive,
            Func<T> packetConstructor) where T : INetSerializable
        {
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                var pkt = packetConstructor();
                pkt.Deserialize(reader);
                onReceive(pkt);
            };
        }

        public void SubscribeNetSerializable<T, TUserData>(
            Action<T, TUserData> onReceive) where T : INetSerializable, new()
        {
            var reference = new T();
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                reference.Deserialize(reader);
                onReceive(reference, (TUserData)userData);
            };
        }

        public void SubscribeNetSerializable<T>(
            Action<T> onReceive) where T : INetSerializable, new()
        {
            var reference = new T();
            _callbacks[GetId<T>()] = (reader, userData) =>
            {
                reference.Deserialize(reader);
                onReceive(reference);
            };
        }

        /// <summary>
        /// Remove any subscriptions by type
        /// </summary>
        /// <typeparam name="T">Packet type</typeparam>
        /// <returns>true if remove is success</returns>
        public bool RemoveSubscription<T>()
        {
            return _callbacks.Remove(GetId<T>());
        }
    }
}

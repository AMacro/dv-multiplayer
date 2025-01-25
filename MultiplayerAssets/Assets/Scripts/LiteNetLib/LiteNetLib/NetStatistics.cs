using System.Collections.Generic;
using System.Threading;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    public sealed class NetStatistics
    {
        private long _packetsSent;
        private long _packetsReceived;
        private long _bytesSent;
        private long _bytesReceived;
        private long _packetLoss;
        private readonly Dictionary<byte, ushort> _packetsWrittenByType = new Dictionary<byte, ushort>(NetPacketProcessor.PacketCount);
        private readonly Dictionary<byte, int> _bytesWrittenByType = new Dictionary<byte, int>(NetPacketProcessor.PacketCount);

        public long PacketsSent => Interlocked.Read(ref _packetsSent);
        public long PacketsReceived => Interlocked.Read(ref _packetsReceived);
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public long PacketLoss => Interlocked.Read(ref _packetLoss);
        public Dictionary<byte, ushort> PacketsWrittenByType => new Dictionary<byte, ushort>(_packetsWrittenByType);
        public Dictionary<byte, int> BytesWrittenByType => new Dictionary<byte, int>(_bytesWrittenByType);

        public long PacketLossPercent {
            get {
                long sent = PacketsSent, loss = PacketLoss;

                return sent == 0 ? 0 : loss * 100 / sent;
            }
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _packetsSent, 0);
            Interlocked.Exchange(ref _packetsReceived, 0);
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            Interlocked.Exchange(ref _packetLoss, 0);
            _packetsWrittenByType.Clear();
            _bytesWrittenByType.Clear();
        }

        public void IncrementPacketsSent()
        {
            Interlocked.Increment(ref _packetsSent);
        }

        public void IncrementPacketsReceived()
        {
            Interlocked.Increment(ref _packetsReceived);
        }

        public void AddBytesSent(long bytesSent)
        {
            Interlocked.Add(ref _bytesSent, bytesSent);
        }

        public void AddBytesReceived(long bytesReceived)
        {
            Interlocked.Add(ref _bytesReceived, bytesReceived);
        }

        public void IncrementPacketLoss()
        {
            Interlocked.Increment(ref _packetLoss);
        }

        public void AddPacketLoss(long packetLoss)
        {
            Interlocked.Add(ref _packetLoss, packetLoss);
        }

        public void IncrementPacketsWritten(byte id)
        {
            if (_packetsWrittenByType.ContainsKey(id))
                _packetsWrittenByType[id]++;
            else
                _packetsWrittenByType[id] = 1;
        }

        public void AddBytesWritten(byte id, int bytesWritten)
        {
            if (_bytesWrittenByType.ContainsKey(id))
                _bytesWrittenByType[id] += bytesWritten;
            else
                _bytesWrittenByType[id] = bytesWritten;
        }

        public override string ToString()
        {
            return
                string.Format(
                    "BytesReceived: {0}\nPacketsReceived: {1}\nBytesSent: {2}\nPacketsSent: {3}\nPacketLoss: {4}\nPacketLossPercent: {5}\n",
                    BytesReceived,
                    PacketsReceived,
                    BytesSent,
                    PacketsSent,
                    PacketLoss,
                    PacketLossPercent);
        }
    }
}

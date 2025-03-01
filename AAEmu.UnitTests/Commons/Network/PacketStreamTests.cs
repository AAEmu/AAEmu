using System.Numerics;

using AAEmu.Commons.Network;

using Xunit;

namespace AAEmu.UnitTests.Commons.Network
{
    public class PacketStreamTests
    {
        [Fact]
        public void WriteAndReadByte_ShouldReturnSameValue()
        {
            var stream = new PacketStream();
            byte expected = 0x7F;
            stream.Write(expected);
            stream.Rollback();

            var result = stream.ReadByte();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteAndReadInt32_ShouldReturnSameValue()
        {
            var stream = new PacketStream();
            var expected = 123456;
            stream.Write(expected);
            stream.Rollback();

            var result = stream.ReadInt32();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WriteAndReadString_ShouldReturnSameString()
        {
            var stream = new PacketStream();
            var expected = "Hello xUnit";
            stream.Write(expected, appendSize: true);
            stream.Rollback();

            var result = stream.ReadString();
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Clear_ShouldResetCountToZero()
        {
            var stream = new PacketStream();
            stream.Write(99);
            stream.Clear();
            Assert.Equal(0, stream.Count);
        }

        [Fact]
        public void Replace_WithByteArray_ShouldCopyDataCorrectly()
        {
            var original = new PacketStream();
            byte value = 100;
            original.PushBack(value);
            
            var newStream = new PacketStream();
            newStream.Replace(original);
            newStream.Rollback();
            Assert.Equal(value, newStream.ReadByte());
        }

        [Fact]
        public void Insert_ShouldInsertBytesIntoTheMiddle()
        {
            var stream = new PacketStream();
            // Запишем два байта: 'A' и 'C'
            stream.Write((byte)'A');
            stream.Write((byte)'C');
            // Вставим 'B' на позицию 1
            stream.Insert(1, new byte[] { (byte)'B' });
            
            stream.Rollback();
            var result = stream.ReadBytes(3);
            var expected = new byte[] { (byte)'A', (byte)'B', (byte)'C' };
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Swap_ShouldExchangeBuffersBetweenStreams()
        {
            var stream1 = new PacketStream();
            var stream2 = new PacketStream();
            stream1.Write((byte)1);
            stream2.Write((byte)2);

            stream1.Swap(stream2);
            
            stream1.Rollback();
            stream2.Rollback();
            Assert.Equal(2, stream1.ReadByte());
            Assert.Equal(1, stream2.ReadByte());
        }

        [Fact]
        public void Clone_ShouldCreateIdenticalPacketStream()
        {
            var stream = new PacketStream();
            stream.Write(99);

            var clone = (PacketStream)stream.Clone();
            stream.Rollback();
            clone.Rollback();
            Assert.Equal(stream.ReadInt32(), clone.ReadInt32());
        }

        [Fact]
        public void CompareTo_ShouldReturnCorrectComparison()
        {
            var stream1 = new PacketStream();
            var stream2 = new PacketStream();
            stream1.Write((byte)1);
            stream2.Write((byte)2);

            // Сравнение должно вернуть значение меньше нуля, если stream1 меньше stream2
            Assert.True(stream1.CompareTo(stream2) < 0);
        }

        [Fact]
        public void WriteAndReadPosition_ShouldReturnSameCoordinates()
        {
            var stream = new PacketStream();
            float x = 1.0f, y = 2.0f, z = 3.0f;
            stream.WritePosition(x, y, z);
            stream.Rollback();

            (var rx, var ry, var rz) = stream.ReadPosition();
            Assert.InRange(rx, x - 0.01f, x + 0.01f);
            Assert.InRange(ry, y - 0.01f, y + 0.01f);
            Assert.InRange(rz, z - 0.01f, z + 0.01f);
        }

        [Fact]
        public void WriteAndReadQuaternionShort_ShouldReturnApproximatelySameQuaternion()
        {
            var stream = new PacketStream();
            // Создаём кватернион с произвольными значениями
            var q = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            stream.WriteQuaternionShort(q);
            stream.Rollback();

            var result = stream.ReadQuaternionShort();
            // Проверяем приближенность для x, y, z, так как w вычисляется через норму
            Assert.InRange(result.X, q.X - 0.1f, q.X + 0.1f);
            Assert.InRange(result.Y, q.Y - 0.1f, q.Y + 0.1f);
            Assert.InRange(result.Z, q.Z - 0.1f, q.Z + 0.1f);
        }
    }
}

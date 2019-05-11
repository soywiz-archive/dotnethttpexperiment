using System;
using System.IO;
using System.Threading.Tasks;

namespace ConsoleApp1
{

    static internal class Tools
    {
        static public void Spawn(Func<Task> task)
        {
            Task.Run(async () =>
            {
                try
                {
                    await task();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                }
            });
        }
    }
    
    public class RingBuffer<T>
    {
        public readonly int Bits;
        private readonly int mask;
        private readonly T[] buffer;
        public int Capacity => buffer.Length;
        public int AvailableRead { get; private set; }
        public int AvailableWrite { get; private set; }

        private int posRead = 0;
        private int posWrite = 0;

        public RingBuffer(int bits)
        {
            this.Bits = bits;
            this.mask = (1 << bits) - 1;
            this.buffer = new T[1 << bits];
            this.AvailableWrite = this.buffer.Length;
        }

        static public RingBuffer<T> CreateWithAtLeast(int count) =>
            new RingBuffer<T>(NextPowerOfTwoBits(count));

        private static int NextPowerOfTwoBits(int count)
        {
            return (int) Math.Ceiling(Math.Log(count) / Math.Log(2));
        }

        public T[] Read(int count)
        {
            var output = new T[count];
            Read(output);
            return output;
        }

        public void Read(T[] data) => Read(data, 0, data.Length);
        public void Write(T[] data) => Write(data, 0, data.Length);

        public void CopyTo(RingBuffer<T> dst) => CopyTo(dst, AvailableRead);

        public void CopyTo(RingBuffer<T> dst, int length)
        {
            var src = this;

            if (src.AvailableRead < length || dst.AvailableWrite < length)
            {
                throw new IndexOutOfRangeException();
            }

            int THIS_MASK = src.mask;
            int THAT_MASK = dst.mask;

            for (var n = 0; n < length; n++)
            {
                src.buffer[(src.posWrite + n) & THIS_MASK] = dst.buffer[(dst.posRead + n) & THAT_MASK];
            }

            src.posWrite = (src.posWrite + length) & THIS_MASK;
            dst.posRead = (dst.posRead + length) & THAT_MASK;
            src.OffsetWrite(+length);
            dst.OffsetWrite(-length);
        }

        public void Read(T[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new IndexOutOfRangeException();
            }

            if (length > AvailableRead)
            {
                throw new IndexOutOfRangeException($"{length} > {AvailableRead}");
            }

            var MASK = this.mask;
            for (var n = 0; n < length; n++)
            {
                data[offset + n] = this.buffer[(posRead + n) & MASK];
            }

            posRead = (posRead + length) & mask;
            OffsetWrite(+length);
        }

        public void Write(T[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new IndexOutOfRangeException();
            }

            if (length > AvailableWrite)
            {
                throw new IndexOutOfRangeException($"{length} > {AvailableWrite}");
            }

            var MASK = this.mask;
            for (var n = 0; n < length; n++)
            {
                this.buffer[(posWrite + n) & MASK] = data[offset + n];
            }

            posWrite = (posWrite + length) & mask;
            OffsetWrite(-length);
        }

        private void OffsetWrite(int Length)
        {
            AvailableWrite += Length;
            AvailableRead -= Length;
        }

        public T this[int index] => this.buffer[(posRead + index) & mask];

        public bool Matches(T[] item, int index)
        {
            for (var n = 0; n < item.Length; n++)
            {
                if (!Equals(this[index + n], item[n])) return false;
            }

            return true;
        }

        public int IndexOf(T item, int start = 0)
        {
            var count = AvailableRead;
            for (var n = start; n < count; n++)
            {
                if (Equals(this[n], item)) return n;
            }

            return -1;
        }

        public int IndexOf(T[] item, int start = 0)
        {
            var count = AvailableRead - item.Length;
            for (var n = start; n <= count; n++)
            {
                if (Matches(item, n)) return n;
            }

            return -1;
        }
    }

    public class FastDeque<T>
    {
        private RingBuffer<T> buffer = new RingBuffer<T>(4);

        public int AvailableRead => buffer.AvailableRead;
        //public int AvailableWrite => buffer.AvailableWrite;

        public void Write(T[] data, int offset, int length)
        {
            if (length > buffer.AvailableWrite)
            {
                var oldBuffer = this.buffer;
                buffer = RingBuffer<T>.CreateWithAtLeast(Math.Max(buffer.Capacity * 2, buffer.AvailableRead + length));
                oldBuffer.CopyTo(buffer);
            }

            buffer.Write(data, offset, length);
        }

        public void Write(T[] data) => Write(data, 0, data.Length);
        public T[] Read(int count) => buffer.Read(count);

        public int IndexOf(T item, int start = 0) => buffer.IndexOf(item, start);
        public int IndexOf(T[] item, int start = 0) => buffer.IndexOf(item, start);
    }

    public interface IAsyncReadable
    {
        Task<byte[]> ReadBytes(int count);
    }

    public class LimitIAsyncReadable : IAsyncReadable
    {
        public readonly IAsyncReadable Parent;
        public readonly long Limit;
        public long Read { get; private set; }
        public long Available => Limit - Read;

        public LimitIAsyncReadable(IAsyncReadable parent, long limit)
        {
            this.Parent = parent;
            this.Limit = limit;
        }

        public async Task<byte[]> ReadBytes(int count)
        {
            var output = await Parent.ReadBytes((int) Math.Min(count, Available));
            Read += output.Length;
            return output;
        }

        public async Task SkipRemaining()
        {
            await this.Skip(Available);
        }
    }

    static class LimitIAsyncReadableExt
    {
        public static LimitIAsyncReadable Limit(this BufferedStreamReader reader, long limit)
        {
            return new LimitIAsyncReadable(reader, limit);
        }

        public static async Task Skip(this IAsyncReadable reader, long count)
        {
            var remaining = count;
            while (remaining > 0)
            {
                var chunk = (int) Math.Min(1024, remaining);
                var data = await reader.ReadBytes(chunk);
                remaining -= data.Length;
            }
        }
    }

    public class BufferedStreamReader : IDisposable, IAsyncReadable
    {
        public readonly Stream socket;
        private FastDeque<byte> buffer = new FastDeque<byte>();

        public BufferedStreamReader(Stream socket)
        {
            this.socket = socket;
        }

        private async Task FillBufferAsync(int count)
        {
            var bytes = new byte[count];
            var temp = new Memory<byte>(bytes);
            var read = await socket.ReadAsync(temp);
            buffer.Write(bytes, 0, read);
        }

        public async Task<byte[]> ReadBytes(int count)
        {
            if (buffer.AvailableRead == 0) await FillBufferAsync(count - buffer.AvailableRead);
            return buffer.Read(Math.Min(count, buffer.AvailableRead));
        }

        public async Task<byte[]> ReadBytesUntil(byte v, int maxLength = int.MaxValue)
        {
            var start = 0;
            while (true)
            {
                if (buffer.AvailableRead >= maxLength)
                {
                    throw new IndexOutOfRangeException();
                }

                //Console.WriteLine($"ReadBytesUntil v={v}, buffer.AvailableRead={buffer.AvailableRead}");
                var index = buffer.IndexOf(v, start);
                //Console.WriteLine($"ReadBytesUntil v={v}, index={index}, buffer.AvailableRead={buffer.AvailableRead}");
                //Console.WriteLine($"ReadBytesUntil v={v}, index={index}, buffer.AvailableRead={buffer.AvailableRead}");
                if (index >= 0)
                {
                    return buffer.Read(index + 1);
                }
                else
                {
                    start = buffer.AvailableRead;
                    //Console.WriteLine($"Filling");
                    await FillBufferAsync(1024);
                    //Console.WriteLine($"Filled");
                }
            }
        }

        public async Task<byte[]> ReadBytesUntil(byte[] v, int maxLength = int.MaxValue)
        {
            var start = 0;
            while (true)
            {
                if (buffer.AvailableRead >= maxLength)
                {
                    throw new IndexOutOfRangeException();
                }

                //Console.WriteLine($"ReadBytesUntil v={v}, buffer.AvailableRead={buffer.AvailableRead}");
                var index = buffer.IndexOf(v, start);
                //Console.WriteLine($"ReadBytesUntil v={v}, index={index}, buffer.AvailableRead={buffer.AvailableRead}");
                if (index >= 0)
                {
                    return buffer.Read(index + v.Length);
                }
                else
                {
                    start = Math.Max(0, buffer.AvailableRead - v.Length);
                    //Console.WriteLine($"Filling");
                    await FillBufferAsync(1024);
                    //Console.WriteLine($"Filled");
                }
            }
        }

        public void Dispose()
        {
            socket.Dispose();
        }
    }
}
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ConsoleApp1;
using NUnit.Framework;

namespace Tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            Assert.Pass();
        }
        
        [Test]
        public void Demo()
        {
            var deque = new FastDeque<byte>();
            Assert.AreEqual(0, deque.AvailableRead);
            deque.Write(new byte[] { 0, 1, 2 });
            Assert.AreEqual(3, deque.AvailableRead);
            deque.Write(new byte[] { 3, 4, 5 });
            Assert.AreEqual(6, deque.AvailableRead);
            Assert.AreEqual(-1, deque.IndexOf(6));
            Assert.AreEqual(0, deque.IndexOf(0));
            Assert.AreEqual(1, deque.IndexOf(1));
            Assert.AreEqual(5, deque.IndexOf(5));
            Assert.AreEqual(new byte[] { 0, 1 }, deque.Read(2));
            Assert.AreEqual(4, deque.AvailableRead);
            Assert.AreEqual(-1, deque.IndexOf(6));
            Assert.AreEqual(-1, deque.IndexOf(0));
            Assert.AreEqual(-1, deque.IndexOf(1));
            Assert.AreEqual(3, deque.IndexOf(5));
        }

        [Test]
        public void demo2()
        {
            Assert.AreEqual(new[]{"a", "b:c"}, "a:b:c".Split(':', 2));
        }

        [Test]
        public async Task TestHandleRequest()
        {
            var output = new MemoryStream();
            
            await HttpServer.HttpHandleAsync(
                new MemoryStream(Encoding.UTF8.GetBytes("GET / HTTP/1.0\r\n\r\n")),
                output,
                async req => new HttpResponse("test")
            );

            Assert.AreEqual(
                "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: Closed\r\n\r\ntest",
                Encoding.UTF8.GetString(output.ToArray())
            );
        }

    }
}
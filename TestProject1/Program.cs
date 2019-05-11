using System.Net;
using System.Threading.Tasks;
using ConsoleApp1;

namespace Tests
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            await HttpServer.HttpServerAsync(IPAddress.Loopback, 8080,
                async req =>
                {
                    return new HttpResponse(
                        $"YES: {req.Method} :: {req.RawPath} :: {req.HttpVersion} :: {req.Headers}");
                });
        }
    }
}
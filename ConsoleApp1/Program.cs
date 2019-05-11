using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    class Program
    {
        static async Task Main(string[] args)
        {
            /*
            await HttpServer.HttpServerAsync(IPAddress.Loopback, 8080,
                async req =>
                {
                    return new HttpResponse(
                        $"YES: {req.Method} :: {req.RawPath} :: {req.HttpVersion} :: {req.Headers}");
                });
                */

            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:8080/");
            listener.Start();
            while (true)
            {
                var context = await listener.GetContextAsync();
                Tools.Spawn(async () =>
                {
                    //Console.WriteLine(Thread.CurrentThread.ManagedThreadId);
                    //await Task.Delay(TimeSpan.FromMilliseconds(5000));
                    await context.Response.OutputStream.WriteAsync(new byte[] { (byte)'Y' });
                    context.Response.Close();
                });
            }
        }
    }
}
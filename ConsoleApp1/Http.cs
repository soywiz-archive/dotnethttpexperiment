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

    public class HttpServer
    {
        
        static public async Task HttpServerAsync(IPAddress address, int port,
            Func<HttpRequest, Task<HttpResponse>> handler, CancellationToken cancel = default(CancellationToken))
        {
            var tcpListener = new TcpListener(address, port);
            tcpListener.Start();
            Console.WriteLine($"Listening to {address}:{port}");
            bool running = true;
            cancel.Register(() => { tcpListener.Stop(); });
            try
            {
                while (true)
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    Tools.Spawn(async () =>
                    {
                        using (var stream = client.GetStream())
                        {
                            await HttpHandleAsync(stream, stream, handler, cancel);
                        }

                        client.Close();
                    });
                }
            }
            catch (SocketException)
            {
            }
        }
        
        static public async Task HttpHandleAsync(Stream input, Stream output,
            Func<HttpRequest, Task<HttpResponse>> handler, CancellationToken cancel = default(CancellationToken))
        {
            //await Task.Delay(TimeSpan.FromMilliseconds(5000));
            //Console.WriteLine(RuntimeHelpers.GetHashCode(client));
            using (var reader = new BufferedStreamReader(input))
            {
                var httpRequest = await reader.ReadLineAsync(8192);
                //Console.WriteLine($"httpRequest: {httpRequest}");
                var headers = new HttpHeaders();
                var headersChars = 0;
                while (true)
                {
                    var rawLine = await reader.ReadLineAsync(8192);
                    headersChars += rawLine.Length;
                    if (headersChars > 8192)
                    {
                        throw new IndexOutOfRangeException("Request headers content is too big");
                    }

                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) break;
                    var parts = line.Split(':', 2);
                    headers.Add(parts[0].Trim(), (parts.Length >= 2) ? parts[1].Trim() : "");
                }

                long contentLength;
                long.TryParse(headers.GetFirst("Content-Length") ?? "0", out contentLength);

                var limited = reader.Limit(contentLength);
                var request = new HttpRequest(httpRequest, headers, limited);
                HttpResponse response;
                try
                {
                    response = await handler(request);
                }
                catch (Exception e)
                {
                    response = new HttpResponse(e.Message, HttpStatus.InternalServerError);
                }

                await limited.SkipRemaining();
                //Console.WriteLine(headers);
                //Console.WriteLine("Host:" + headers.GetFirst("host"));

                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {response.Status.Code} {response.Status.Message}\r\n");
                response.Headers.Replace("Content-Length", response.Body.Length.ToString());
                response.Headers.Replace("Connection", "Closed");
                foreach (var pair in response.Headers.Items)
                {
                    sb.Append($"{pair.Key}: {pair.Value}\r\n");
                }

                sb.Append("\r\n");
                await output.WriteAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(sb.ToString())),
                    cancel);
                await response.Body.WriteAsync(output);
            }
        }

    }

    internal static class Extensions
    {
        public static async Task<string>
            ReadLineAsync(this BufferedStreamReader stream, int maxLength = int.MaxValue) =>
            Encoding.UTF8.GetString(await stream.ReadBytesUntil((byte) '\n', maxLength));

        public static string ToListString<T>(this List<T> list) => "[" + String.Join(", ", list) + ";";
    }

    public struct HttpRequest
    {
        public readonly string[] HttpLineParts;
        public readonly string HttpLine;
        public readonly HttpHeaders Headers;
        public readonly IAsyncReadable Body;
        public string Method => HttpLineParts[0];
        public string RawPath => (HttpLineParts.Length >= 2) ? HttpLineParts[1] : "";
        public string HttpVersion => (HttpLineParts.Length >= 3) ? HttpLineParts[2] : "HTTP/1.0";

        public HttpRequest(string httpLine, HttpHeaders headers, IAsyncReadable body)
        {
            HttpLine = httpLine;
            HttpLineParts = HttpLine.Split(" ", 3);
            Headers = headers;
            Body = body;
        }
    }

    public struct HttpStatus
    {
        public readonly int Code;
        public readonly string Message;

        public HttpStatus(int code, string message = null)
        {
            Code = code;
            Message = message ?? GetDefaultMessage(code);
        }

        public static HttpStatus Ok => new HttpStatus(200);
        public static HttpStatus InternalServerError => new HttpStatus(500);

        public static string GetDefaultMessage(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 500: return "Internal Server Error";
                default: return code.ToString();
            }
        }
    }

    public struct HttpResponse
    {
        public readonly HttpStatus Status;
        public readonly HttpHeaders Headers;
        public readonly IHttpBody Body;

        public HttpResponse(object body, HttpStatus? status = null, HttpHeaders headers = null)
        {
            Status = status ?? HttpStatus.Ok;
            Headers = headers ?? new HttpHeaders();
            switch (body)
            {
                case IHttpBody httpBody:
                    Body = httpBody;
                    break;
                case byte[] bytes:
                    Body = new MemoryHttpBody(bytes);
                    break;
                case Stream stream:
                    Body = new StreamHttpBody(stream);
                    break;
                case FileInfo fileInfo:
                    Body = new FileHttpBody(fileInfo);
                    break;
                default:
                    Body = new MemoryHttpBody(body.ToString());
                    break;
            }
        }
    }

    class MemoryHttpBody : IHttpBody
    {
        public readonly byte[] Bytes;

        public long Length => Bytes.Length;

        public MemoryHttpBody(byte[] bytes) => Bytes = bytes;

        public MemoryHttpBody(string str, Encoding encoding = null) =>
            Bytes = (encoding ?? Encoding.UTF8).GetBytes(str);

        public async Task WriteAsync(Stream stream)
        {
            await stream.WriteAsync(this.Bytes);
        }
    }

    class StreamHttpBody : IHttpBody
    {
        public readonly Stream Stream;

        public long Length => Stream.Length;

        public StreamHttpBody(Stream stream) => Stream = stream;

        public async Task WriteAsync(Stream stream)
        {
            await this.Stream.CopyToAsync(stream);
        }
    }

    class FileHttpBody : IHttpBody
    {
        public readonly FileInfo FileInfo;

        public long Length => FileInfo.Length; // @TODO: Should we move this to something else to prevent blocking?

        public FileHttpBody(FileInfo fileInfo) => FileInfo = fileInfo;

        public async Task WriteAsync(Stream stream)
        {
            using (var fileStream = FileInfo.OpenRead())
            {
                await fileStream.CopyToAsync(stream);
            }
        }
    }

    public interface IHttpBody
    {
        long Length { get; }
        Task WriteAsync(Stream stream);
    }

    public class HttpHeaders
    {
        public List<KeyValuePair<string, string>> Items;

        public HttpHeaders()
        {
            Items = new List<KeyValuePair<string, string>>();
        }

        public HttpHeaders(List<KeyValuePair<string, string>> items)
        {
            Items = items;
        }

        public string GetFirst(string key) => GetFirstPair(key)?.Value;

        public KeyValuePair<string, string>? GetFirstPair(string key)
        {
            foreach (var item in Items)
            {
                if (String.Equals(item.Key, key, StringComparison.InvariantCultureIgnoreCase)) return item;
            }

            return null;
        }

        public void Add(string key, string value)
        {
            Items.Add(new KeyValuePair<string, string>(key, value));
        }

        public void Replace(string key, string value)
        {
            // @TODO: Properly implement this
            Add(key, value);
        }

        public override string ToString() => Items.ToListString();
    }
}
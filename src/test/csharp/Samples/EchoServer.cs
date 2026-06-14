using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

using Structmap;

namespace Samples;

public static class EchoServer
{
    private static readonly ILogger _logger;

    static EchoServer()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = loggerFactory.CreateLogger(nameof(EchoServer));
    }

    public static async Task Run()
    {
        var server = new WebTransportServer(8443, "cert.pem", "key.pem")
        {
            ChannelFactory = () =>
            {
                var n = 10;
                return Channel.CreateBounded<Object>(n);
            },
            SessionHandler = async (ch) =>
            {
                await foreach (var e in ch.Reader.ReadAllAsync())
                {
                    if (e is Start start)
                    {
                        _logger.LogInformation("Session started 0x{session:x}", (IntPtr)start.Session.Identifier);
                    }

                    if (e is Datagram d)
                    {
                        _logger.LogInformation("Ready to echo payload {data}", Encoding.ASCII.GetString(d.Payload));
                        d.Session.Server.Send(new Datagram(d.Session, d.Payload));
                    }

                    if (e is Structmap.Stream s)
                    {
                        _logger.LogInformation("Ready to echo stream 0x{session:x}", (IntPtr)s.Identifier);
                        // await s.Incoming.CopyToAsync(Console.OpenStandardOutput());
                        await s.Incoming.CopyToAsync(s.Outgoing);
                    }

                    if (e is End end)
                    {
                        _logger.LogInformation("Session ending 0x{session:x}", (IntPtr)end.Session.Identifier);
                        break;
                    }
                }

                return null;
            },
            Logger = _logger
        };

        var tokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, _) => {
            _logger.LogInformation("Shutting down...");
            if (!server.Stop())
            {
                _logger.LogError("Failed to stop server");
                Environment.Exit(1);
            }
            tokenSource.Cancel();
            Environment.Exit(0);
        };

        if (!server.Start())
        {
            _logger.LogError("Failed to start server");
            Environment.Exit(1);
        }

        await Task.Delay(Timeout.Infinite, tokenSource.Token);
    }
}
using System.Text;
using System.Threading.Channels;
using Structmap;

namespace Samples;

public static class EchoServer
{
    public static async Task Main(string[] args)
    {
        var server = new WebTransportServer(8443, "cert.pem", "key.pem")
        {
            ChannelFactory = () =>
            {
                var n = 10;
                return Channel.CreateBounded<Object>(n);
            },
            Handler = async (ch) =>
            {
                await foreach (var e in ch.Reader.ReadAllAsync())
                {
                    if (e is Datagram d)
                    {
                        Console.Out.WriteLine("[HANDLER] Ready to echo payload {0}", Encoding.ASCII.GetString(d.Payload));
                        d.Context.Server.Send(d.Context.Identifier, d.Payload);
                    }

                    if (e is Structmap.Stream s)
                    {
                        Console.Out.WriteLine("[HANDLER] Ready to echo stream 0x{0:x}", (IntPtr)s.Identifier);
                        // await s.Incoming.CopyToAsync(Console.OpenStandardOutput());
                        await s.Incoming.CopyToAsync(s.Outgoing);
                    }
                }
            }
        };

        var tokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, _) => {
            Console.Out.WriteLine("Shutting down...");
            if (!server.Stop())
            {
                Console.Out.WriteLine("Failed to stop server");
                Environment.Exit(1);
            }
            tokenSource.Cancel();
            Environment.Exit(0);
        };

        if (!server.Start())
        {
            Console.Out.WriteLine("Failed to start server");
            Environment.Exit(1);
        }

        await Task.Delay(Timeout.Infinite, tokenSource.Token);
    }
}
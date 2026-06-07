namespace Samples;

public static class Entrypoint
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0 || nameof(EchoServer).Equals(args[0]))
        {
            await EchoServer.Run();
        }
        else
        {
            await Console.Error.WriteLineAsync($"Sample {args[0]} not found");
            Environment.Exit(1);
        }
    }
}
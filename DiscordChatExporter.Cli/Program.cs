using System.Threading.Tasks;
using CliFx;
using DiscordChatExporter.Commanding;

namespace DiscordChatExporter.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args) =>
        await CommandApplication
            .CreateBuilder()
            .AddCommandsFromThisAssembly()
            .Build()
            .RunAsync(args);
}

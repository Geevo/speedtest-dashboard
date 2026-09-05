using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SpeedtestDashboard.ProcessFixture;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.InputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    return 2;
}

switch (args[0])
{
    case "echo-args":
        Console.Write(JsonSerializer.Serialize(args.Skip(1).ToArray()));
        return 0;

    case "write-stdout":
        Console.Write(new string('o', ParseCount(args)));
        return 0;

    case "write-stderr":
        Console.Error.Write(new string('e', ParseCount(args)));
        return 0;

    case "write-both":
        var count = ParseCount(args);
        var stdout = Task.Run(() => Console.Write(new string('o', count)));
        var stderr = Task.Run(() => Console.Error.Write(new string('e', count)));
        await Task.WhenAll(stdout, stderr);
        return 0;

    case "invalid-utf8":
        await Console.OpenStandardOutput().WriteAsync(new byte[] { 0x66, 0x6f, 0x80, 0x6f });
        return 0;

    case "sleep":
        await Task.Delay(ParseCount(args));
        return 0;

    case "exit":
        return ParseCount(args);

    case "working-directory":
        Console.Write(Directory.GetCurrentDirectory());
        return 0;

    case "spawn-child":
        using (var child = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }.WithArguments(typeof(FixtureMarker).Assembly.Location, "sleep", "30000")))
        {
            if (child is null)
            {
                return 3;
            }

            Console.WriteLine(child.Id);
            Console.Out.Flush();
            await Task.Delay(30000);
        }

        return 0;

    default:
        return 2;
}

static int ParseCount(string[] arguments) =>
    arguments.Length > 1 && int.TryParse(arguments[1], out var value) ? value : 0;

internal static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

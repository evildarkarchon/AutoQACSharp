using System.Diagnostics;

if (args.Length == 0)
{
    return 2;
}

switch (args[0])
{
    case "sleep":
        await Task.Delay(ParseMilliseconds(args));
        return 0;

    case "exit-on-stdin":
        Console.WriteLine("READY");
        await Console.In.ReadLineAsync();
        return 0;

    case "spawn-child":
        await SpawnChildAsync(ParseMilliseconds(args));
        return 0;

    default:
        return 2;
}

static int ParseMilliseconds(string[] args) =>
    args.Length > 1 && int.TryParse(args[1], out var value) ? value : 30_000;

static async Task SpawnChildAsync(int milliseconds)
{
    var currentExe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
    using var child = Process.Start(new ProcessStartInfo
    {
        FileName = currentExe,
        Arguments = $"sleep {milliseconds}",
        UseShellExecute = false,
        CreateNoWindow = true
    });

    if (child is null)
    {
        return;
    }

    await child.WaitForExitAsync();
}

using System;
using DarkRift.Server;

public class CommandTestPlugin : Plugin
{
    public override Version Version => new Version(1, 0, 0);
    public override bool ThreadSafe => true;

    public override Command[] Commands => new[]
    {
        new Command("testcmd", "A test command.", "testcmd", HandleTestCommand)
    };

    public CommandTestPlugin(PluginLoadData pluginLoadData) : base(pluginLoadData) { }

    private void HandleTestCommand(object sender, CommandEventArgs e)
    {
        Console.WriteLine("Test command executed!");
        Console.WriteLine($"Arguments: {string.Join(", ", e.Arguments)}");
    }
}

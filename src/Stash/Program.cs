namespace Stash;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Placeholders.Install();
#if CLI_BUILD
        return Cli.Dispatch(args.Length == 0 ? new[] { "help" } : args, Console.Out, Console.Error);
#else
        if (args.Length > 0 && args[0] != "--tray") return Cli.Run(args);
        App.StartHidden = args.Length > 0 && args[0] == "--tray";
        var app = new App();
        app.InitializeComponent();
        return app.Run();
#endif
    }
}

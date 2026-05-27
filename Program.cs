using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;
using Spectre.Console.Cli;
using symlinker.Resources;

internal class Program
{
    private const string MenuKey = @"AllFilesystemObjects\shell\CreateSymlink";

    [STAThread]
    public static int Main(string[] args)
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.AddCommand<InstallCommand>("install");
            config.AddCommand<UninstallCommand>("uninstall");
            config.AddCommand<LinkCommand>("link");
        });

        return app.Run(args);
    }

    // ---------------- CORE LOGIC ----------------

    private static int Install()
    {
        string exe = Environment.ProcessPath!;

        using var key = Registry.ClassesRoot.CreateSubKey(MenuKey);
        key.SetValue("", Resources.ResourceManager.GetString("ShellEntry")!);
        key.SetValue("Icon", exe);
        key.SetValue("HasLUAShield", "");

        using var cmd = key.CreateSubKey("command");
        cmd.SetValue("", $"\"{exe}\" link \"%1\"");

        return 0;
    }

    private static int Uninstall()
    {
        Registry.ClassesRoot.DeleteSubKeyTree(MenuKey, false);
        return 0;
    }

    private static string? PickDestinationFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Resources.ResourceManager.GetString("SelectDest")!,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private static int CreateSymlink(string target, bool allowElevation)
    {
        string full = Path.GetFullPath(target);

        if (!File.Exists(full) && !Directory.Exists(full))
        {
            MessageBox.Show(Resources.ResourceManager.GetString("Error.TargetNotFound")!);
            return 1;
        }

        string name = Path.GetFileName(full);

        string? destDir = PickDestinationFolder();

        if (string.IsNullOrWhiteSpace(destDir))
            return 1;

        string linkPath = Path.Combine(destDir, name);

        if (File.Exists(linkPath) || Directory.Exists(linkPath))
        {
            MessageBox.Show(Resources.ResourceManager.GetString("Error.DestExists")!);
            return 1;
        }

        try
        {
            if (Directory.Exists(full))
                Directory.CreateSymbolicLink(linkPath, full);
            else
                File.CreateSymbolicLink(linkPath, full);

            MessageBox.Show(
                Resources.ResourceManager.GetString("Success")!,
                "Symlinker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly | MessageBoxOptions.ServiceNotification
            );
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            if (!allowElevation)
                return 1;

            RelaunchElevated(full);
            return 0;
        }
    }

    private static void RelaunchElevated(string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            Arguments = $"link \"{target}\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(psi);
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ---------------- COMMANDS ----------------

    private sealed class InstallCommand : Command<InstallCommand.Settings>
    {
        public sealed class Settings : CommandSettings { }

        protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            EnsureAdminOrRelaunch("install");

            var result = Install();

            MessageBox.Show(
                Resources.ResourceManager.GetString(
                    result == 0 ? "Install.Success" : "Install.Fail"),
                "Symlinker",
                MessageBoxButtons.OK,
                result == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error
            );

            return result;
        }
    }

    private sealed class UninstallCommand : Command<UninstallCommand.Settings>
    {
        public sealed class Settings : CommandSettings { }

        protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            EnsureAdminOrRelaunch("uninstall");

            var result = Uninstall();

            MessageBox.Show(
                Resources.ResourceManager.GetString(
                    result == 0 ? "Uninstall.Success" : "Uninstall.Fail"),
                "Symlinker",
                MessageBoxButtons.OK,
                result == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error
            );

            return result;
        }
    }

    private sealed class LinkCommand : Command<LinkCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<TARGET>")]
            public string Target { get; set; } = "";
        }

        protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            return CreateSymlink(settings.Target, allowElevation: true);
        }
    }

    // ---------------- ELEVATION ----------------

    private static void EnsureAdminOrRelaunch(string mode)
    {
        if (IsAdmin())
            return;

        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath!,
            Arguments = mode,
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(psi);
        Environment.Exit(0);
    }
}
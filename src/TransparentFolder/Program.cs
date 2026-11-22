using System.Diagnostics;

namespace TransparentFolder;

internal static class Program
{
    private const string MutexName = "Global\\TransparentFolder_SingleInstance_Mutex_v1";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            RunUninstall();
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new FolderManager());
    }

    private static void RunUninstall()
    {
        StopOtherProcesses("TransparentFolder");
        StopOtherProcesses("DesktopFolders");
        FolderStore.ApplyAutoStart(false);
        ShellNewRegistrar.Unregister();
    }

    private static void StopOtherProcesses(string processName)
    {
        int currentPid = Environment.ProcessId;
        foreach (Process proc in Process.GetProcessesByName(processName))
        {
            using (proc)
            {
                if (proc.Id == currentPid)
                {
                    continue;
                }

                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                catch
                {
                    // Ignore inaccessible or already-exited processes
                }
            }
        }
    }
}

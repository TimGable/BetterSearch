using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BetterSearch;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "BetterSearch.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogException("Unhandled domain exception", args.ExceptionObject as Exception);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        try
        {
            Log("Starting BetterSearch.");
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            LogException("Startup failed", ex);
            MessageBox.Show(
                $"BetterSearch could not start. Details were written to:{Environment.NewLine}{LogPath}",
                "BetterSearch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"Exiting BetterSearch with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred. Details were written to:{Environment.NewLine}{LogPath}",
            "BetterSearch",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:u} {message}{Environment.NewLine}");
        }
        catch
        {
            // If the log path is blocked, keep going. A failed log write should not break startup.
        }
    }

    private static void LogException(string message, Exception? exception)
    {
        Log($"{message}: {exception}");
    }
}

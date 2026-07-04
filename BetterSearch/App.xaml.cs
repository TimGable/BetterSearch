using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BetterSearch;

public partial class App : Application
{
    // Keep the log beside the executable so published builds can be diagnosed without extra setup.
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "BetterSearch.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Catch both background and UI-thread failures so the app can leave a useful log behind.
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
            ShowThemedError(
                $"BetterSearch could not start. Details were written to:{Environment.NewLine}{LogPath}",
                "BetterSearch");
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
        ShowThemedError(
            $"An unexpected error occurred. Details were written to:{Environment.NewLine}{LogPath}",
            "BetterSearch");
        e.Handled = true;
    }

    private static void ShowThemedError(string message, string title)
    {
        // Use an app-owned dialog so startup errors match the dark UI instead of the default Windows message box.
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 190,
            MinWidth = 420,
            MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(21, 21, 21)),
            Foreground = new SolidColorBrush(Color.FromRgb(242, 242, 242)),
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(242, 242, 242))
        };
        Grid.SetRow(heading, 0);

        var details = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(183, 183, 183))
        };
        Grid.SetRow(details, 1);

        var okButton = new Button
        {
            Content = "OK",
            Width = 84,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 74, 74)),
            Foreground = new SolidColorBrush(Color.FromRgb(242, 242, 242))
        };
        okButton.Click += (_, _) => window.Close();
        Grid.SetRow(okButton, 2);

        root.Children.Add(heading);
        root.Children.Add(details);
        root.Children.Add(okButton);
        window.Content = root;
        window.ShowDialog();
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

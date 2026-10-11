using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace SoundPanel;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "1Flow", "SoundPanel", "crash.log");

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            ReportCrash(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ReportCrash(e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var saved = new SoundPanel.Services.SettingsService().Load();
        SoundPanel.Services.ThemeService.ApplyAccent(saved.AccentTheme);
        SoundPanel.Services.ThemeService.ApplyRobot(saved.RobotTheme);
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
            Shutdown(1);
        }
    }

    private static void ReportCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
        }
        catch { /* ignore logging failures */ }

        MessageBox.Show(
            $"{ex.GetType().Name}: {ex.Message}\n\nLog: {LogPath}",
            "1Flow SoundPanel",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

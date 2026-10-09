using System.ComponentModel;
using System.Windows;

namespace HowlingWhispers.CodaLauncher;

public partial class UpdateWindow : Window
{
    private readonly LauncherUpdateInfo _update;
    private readonly bool _resumePlayAfterUpdate;
    private readonly CancellationTokenSource _cts = new();
    private PreparedLauncherUpdate? _prepared;
    private string? _lastStage;
    private bool _started;
    private bool _saidQuarter;
    private bool _saidHalf;
    private bool _saidAlmost;

    public bool RestartRequested { get; private set; }
    public bool ContinueWithoutUpdate { get; private set; }

    internal UpdateWindow(LauncherUpdateInfo update, bool resumePlayAfterUpdate = false)
    {
        _update = update;
        _resumePlayAfterUpdate = resumePlayAfterUpdate;
        InitializeComponent();
        VersionText.Text = $"CodaLauncher {App.LauncherVersion}  ->  {update.Version}";
        if (_resumePlayAfterUpdate)
            ContinueButton.Content = "PLAY WITHOUT UPDATE";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;

        AppendLine("CodaLauncher update terminal");
        AppendLine($"session: {App.LauncherVersion} -> {_update.Version}");
        AppendLine("");
        AppendLine("> Coda: Hold that paw. I found a newer launcher.");
        AppendLine("> Coda: No, you do not need to hunt down an installer. That's my paperwork.");
        AppendLine("");

        try
        {
            _prepared = await SelfUpdater.DownloadAndStageAsync(
                _update,
                ReportProgress,
                _cts.Token);

            AppendLine("");
            AppendLine("> Coda: Fresh files, tidy clipboard.");
            DownloadProgress.Value = 100;
            if (_resumePlayAfterUpdate)
            {
                AppendLine("> Coda: Restarting with the fresh version, then loading Minecraft.");
                RestartLauncher();
            }
            else
            {
                AppendLine("> Coda: Hit REBOOT CODALAUNCHER to finish installing.");
                StatusText.Text = "Update ready. Waiting for your reboot.";
                RebootButton.IsEnabled = true;
                RebootButton.Focus();
            }
        }
        catch (OperationCanceledException)
        {
            // The player closed the updater. The installed launcher was never replaced.
        }
        catch (Exception ex)
        {
            AppendLine("");
            AppendLine("! UPDATE STOPPED");
            AppendLine("> Coda: I couldn't finish that update. Your installed launcher is untouched.");
            AppendLine($"> {ex.Message}");

            StatusText.Text = "Coda couldn't finish the update. The installed launcher is untouched.";
            RebootButton.Visibility = Visibility.Collapsed;
            ContinueButton.Visibility = Visibility.Visible;
            ContinueButton.Focus();
        }
    }

    private void ReportProgress(LauncherUpdateProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyProgress(progress));
            return;
        }
        ApplyProgress(progress);
    }

    private void ApplyProgress(LauncherUpdateProgress progress)
    {
        StatusText.Text = progress.Message;
        if (progress.Percent is int percent)
            DownloadProgress.Value = percent;

        if (!string.Equals(_lastStage, progress.Stage, StringComparison.Ordinal))
        {
            _lastStage = progress.Stage;
            switch (progress.Stage)
            {
                case "download":
                    AppendLine("> Coda: Found the update package. Bringing it in now.");
                    break;
                case "verify":
                    AppendLine("> Coda: Download caught. Checking the official pawprint...");
                    break;
                case "stage":
                    AppendLine("> Coda: Pawprint confirmed. Filing the fresh bits where they belong.");
                    break;
                case "ready":
                    AppendLine("> Coda: Everything important has a checkbox now.");
                    break;
            }
        }

        if (progress.Stage == "download" && progress.Percent is int download)
        {
            if (download >= 25 && !_saidQuarter)
            {
                _saidQuarter = true;
                AppendLine("> Coda: Quarter of the box unpacked. Nothing alarming has fallen out.");
            }
            if (download >= 50 && !_saidHalf)
            {
                _saidHalf = true;
                AppendLine("> Coda: Halfway. The paperwork is behaving. Suspicious, but acceptable.");
            }
            if (download >= 80 && !_saidAlmost)
            {
                _saidAlmost = true;
                AppendLine("> Coda: Nearly there. No loose files under the desk.");
            }
        }
    }

    private void Reboot_Click(object sender, RoutedEventArgs e) => RestartLauncher();

    private void RestartLauncher()
    {
        if (_prepared is null) return;

        try
        {
            RebootButton.IsEnabled = false;
            ContinueButton.IsEnabled = false;
            StatusText.Text = "Rebooting CodaLauncher...";
            AppendLine("");
            AppendLine("> Coda: Right. Fresh clipboard. See you on the other side.");

            SelfUpdater.StartApplyAndRestart(_prepared, _resumePlayAfterUpdate);
            RestartRequested = true;
            Close();
        }
        catch (Exception ex)
        {
            AppendLine("");
            AppendLine("! REBOOT FAILED");
            AppendLine("> Coda: The update is staged, but Windows wouldn't let me start the handoff.");
            AppendLine($"> {ex.Message}");

            StatusText.Text = "Reboot handoff failed. Your installed launcher is untouched.";
            RebootButton.IsEnabled = true;
            ContinueButton.Visibility = Visibility.Visible;
            ContinueButton.IsEnabled = true;
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        ContinueWithoutUpdate = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!RestartRequested)
            _cts.Cancel();
    }

    private void AppendLine(string line)
    {
        Terminal.AppendText(line + Environment.NewLine);
        Terminal.ScrollToEnd();
    }
}

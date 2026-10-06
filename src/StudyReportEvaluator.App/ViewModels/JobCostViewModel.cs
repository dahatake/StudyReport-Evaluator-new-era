using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using StudyReportEvaluator.App.Usage;

namespace StudyReportEvaluator.App.ViewModels;

/// <summary>Receives only a validated, existing owned log file or its directory.</summary>
public interface IJobCostFileLauncher
{
    void Open(string absolutePath);
}

/// <summary>UI-thread projection of a job snapshot; the host owns job generation admission.</summary>
public sealed class JobCostViewModel : UiObservableObject
{
    public const string NoDataText = "この実行のコスト記録なし";
    private readonly IJobCostFileLauncher launcher;
    private readonly string jobsDirectory;
    private readonly ViewModelCommand openLogCommand;
    private readonly ViewModelCommand openDirectoryCommand;
    private JobCostSnapshot? snapshot;
    private bool isExpanded;
    private bool autoFollow = true;
    private string openStatusText = string.Empty;

    public JobCostViewModel()
        : this(new AssociatedFileLauncher(), DefaultJobsDirectory())
    {
    }

    public JobCostViewModel(IJobCostFileLauncher launcher, string jobsDirectory)
    {
        this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        ArgumentException.ThrowIfNullOrWhiteSpace(jobsDirectory);
        if (!Path.IsPathFullyQualified(jobsDirectory))
        {
            throw new ArgumentException("An absolute owned jobs directory is required.", nameof(jobsDirectory));
        }

        this.jobsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(jobsDirectory));
        openLogCommand = new ViewModelCommand(_ => Open(directory: false), _ => GetOwnedLogPath() is not null);
        openDirectoryCommand = new ViewModelCommand(_ => Open(directory: true), _ => GetOwnedLogPath() is not null);
    }

    public JobCostSnapshot? Snapshot => snapshot;
    public string SummaryText => snapshot?.SummaryText ?? NoDataText;
    public string DetailsText => snapshot?.DetailsText ?? NoDataText;
    public string LogText => snapshot?.LogText ?? NoDataText;
    public string? LogPath => snapshot?.LogPath;
    public string LogStatusText => snapshot?.LogStatusText ?? NoDataText;
    public string OpenStatusText => openStatusText;
    public ICommand OpenLogCommand => openLogCommand;
    public ICommand OpenDirectoryCommand => openDirectoryCommand;

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool AutoFollow
    {
        get => autoFollow;
        set => SetProperty(ref autoFollow, value);
    }

    public void Apply(JobCostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (this.snapshot is { } previous && previous.JobId == snapshot.JobId
            && snapshot.Revision <= previous.Revision)
        {
            return;
        }

        if (this.snapshot?.JobId != snapshot.JobId)
        {
            openStatusText = string.Empty;
        }

        this.snapshot = snapshot;
        NotifySnapshotChanged();
    }

    public void Reset()
    {
        snapshot = null;
        openStatusText = string.Empty;
        IsExpanded = false;
        NotifySnapshotChanged();
    }

    private void NotifySnapshotChanged()
    {
        OnPropertiesChanged(nameof(Snapshot), nameof(SummaryText), nameof(DetailsText),
            nameof(LogText), nameof(LogPath), nameof(LogStatusText), nameof(OpenStatusText));
        openLogCommand.RaiseCanExecuteChanged();
        openDirectoryCommand.RaiseCanExecuteChanged();
    }

    // No IO in notification or CanExecute paths. Reconstruct the target from the
    // generated job identity, not from shell/URI text supplied by a snapshot.
    private string? GetOwnedLogPath()
    {
        if (snapshot is not { LogPath: { } path } current || current.JobId == Guid.Empty
            || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            string fileName = Path.GetFileName(path);
            string? format = fileName == $"{current.JobId:N}.jsonl" ? "N"
                : fileName == $"{current.JobId:D}.jsonl" ? "D" : null;
            if (format is null)
            {
                return null;
            }

            string expected = Path.Combine(jobsDirectory, current.JobId.ToString(format) + ".jsonl");
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // Reject traversal spellings even when normalization would land inside jobs.
            return string.Equals(path, expected, comparison) ? expected : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void Open(bool directory)
    {
        try
        {
            string? path = GetOwnedLogPath();
            if (path is null || !Directory.Exists(jobsDirectory)
                || (!directory && !File.Exists(path)))
            {
                SetOpenStatus("ログまたは保存先がありません。ログの保存状態を確認してください。");
                return;
            }

            // Do not follow symlinks/junctions out of the owned location.
            for (DirectoryInfo? item = new(jobsDirectory); item is not null; item = item.Parent)
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    SetOpenStatus("リンクされたログ保存先は開けません。");
                    return;
                }
            }

            if (!directory && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                SetOpenStatus("リンクされたログファイルは開けません。");
                return;
            }

            launcher.Open(directory ? jobsDirectory : path);
            SetOpenStatus("既定のアプリへ開く操作を渡しました。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException
            or System.Security.SecurityException)
        {
            // Do not expose exception text, paths, command lines, or user information.
            SetOpenStatus("開けませんでした。アクセス権と既定のアプリを確認してください。");
        }
    }

    private void SetOpenStatus(string text)
    {
        openStatusText = text;
        OnPropertyChanged(nameof(OpenStatusText));
    }

    private static string DefaultJobsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyReportEvaluator", "jobs");

    private sealed class AssociatedFileLauncher : IJobCostFileLauncher
    {
        public void Open(string absolutePath)
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = absolutePath,
                UseShellExecute = true,
                Verb = "open",
            });
        }
    }
}
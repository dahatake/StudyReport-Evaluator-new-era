using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace StudyReportEvaluator.App.Views;

public interface IResumeCheckpointPicker
{
    Task<string?> PickAsync(TopLevel topLevel);
}

public sealed class ResumeCheckpointPickerPathUnavailableException : Exception
{
    public ResumeCheckpointPickerPathUnavailableException()
        : base("The selected checkpoint does not expose a local path.")
    {
    }
}

public sealed class NativeResumeCheckpointPicker : IResumeCheckpointPicker
{
    private static readonly FilePickerFileType PartialXlsx = new("中断した run の checkpoint (.partial.xlsx)")
    {
        Patterns = ["*.partial.xlsx"],
        AppleUniformTypeIdentifiers = ["org.openxmlformats.spreadsheetml.sheet"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
    };

    public async Task<string?> PickAsync(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "再開する .partial.xlsx を選択",
                AllowMultiple = false,
                FileTypeFilter = [PartialXlsx],
                SuggestedFileType = PartialXlsx,
            });
        if (files.Count == 0)
        {
            return null;
        }

        return files[0].TryGetLocalPath()
            ?? throw new ResumeCheckpointPickerPathUnavailableException();
    }

    public override string ToString() =>
        $"{nameof(NativeResumeCheckpointPicker)} {{ Content = <redacted> }}";
}
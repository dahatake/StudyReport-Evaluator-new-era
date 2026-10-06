using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using StudyReportEvaluator.App.Launch;

namespace StudyReportEvaluator.App.Views;

public sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(LaunchOptionsException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Title = "StudyReport Evaluator - 起動error";
        Width = 560;
        Height = 260;
        MinWidth = 420;
        MinHeight = 220;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TextBlock code = new()
        {
            Name = "StartupErrorCode",
            Text = error.Code,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        AutomationProperties.SetAutomationId(code, "StartupErrorCode");
        TextBlock guidance = new()
        {
            Text = Guidance(error.Code),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        Button close = new()
        {
            Name = "CloseStartupError",
            Content = "終了",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 96,
        };
        AutomationProperties.SetAutomationId(close, "CloseStartupError");
        close.Click += (_, _) => Close();
        ScrollViewer scrollViewer = new()
        {
            Name = "StartupErrorScrollViewer",
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "起動optionを確認してください",
                        FontSize = 22,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                    },
                    code,
                    guidance,
                    close,
                },
            },
        };
        AutomationProperties.SetAutomationId(scrollViewer, "StartupErrorScrollViewer");
        Content = scrollViewer;
    }

    public override string ToString() =>
        $"{nameof(StartupErrorWindow)} {{ Content = <redacted> }}";

    private static string Guidance(string code) => code switch
    {
        LaunchErrorCodes.UnknownOption => "使用できるoptionは --input と反復可能な --prompt だけです。",
        LaunchErrorCodes.MissingValue => "optionの直後にfile pathを指定してください。",
        LaunchErrorCodes.DuplicateInput => "--inputは0回または1回だけ指定できます。",
        LaunchErrorCodes.PromptExtensionInvalid => "Prompt fileにはUTF-8 plain textの .txt を指定してください。",
        LaunchErrorCodes.PromptUtf8Invalid => "Prompt fileをUTF-8 plain textとして保存し直してください。",
        LaunchErrorCodes.PromptLengthInvalid => "Prompt fileは1～32,767文字にしてください。",
        LaunchErrorCodes.PromptReadFailed => "Prompt fileの存在と読み取り権限を確認してください。",
        _ => "指定したpathを確認してください。",
    };
}

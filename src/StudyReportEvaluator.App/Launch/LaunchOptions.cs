using System.Collections.Immutable;
using System.Text;

namespace StudyReportEvaluator.App.Launch;

public static class LaunchErrorCodes
{
    public const string UnknownOption = "LAUNCH_UNKNOWN_OPTION";
    public const string MissingValue = "LAUNCH_MISSING_VALUE";
    public const string DuplicateInput = "LAUNCH_DUPLICATE_INPUT";
    public const string InvalidPath = "LAUNCH_PATH_INVALID";
    public const string PromptExtensionInvalid = "PROMPT_EXTENSION_INVALID";
    public const string PromptReadFailed = "PROMPT_READ_FAILED";
    public const string PromptUtf8Invalid = "PROMPT_UTF8_INVALID";
    public const string PromptLengthInvalid = "PROMPT_LENGTH_INVALID";
}

public sealed class LaunchOptionsException : Exception
{
    public LaunchOptionsException(string code, string field)
        : base("The application launch options are invalid.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string Field { get; }

    public override string ToString() =>
        $"{nameof(LaunchOptionsException)} {{ Code = {Code}, Field = {Field}, Content = <redacted> }}";
}

public sealed class LaunchOptions
{
    private LaunchOptions(string? inputPath, ImmutableArray<string> promptPaths)
    {
        InputPath = inputPath;
        PromptPaths = promptPaths;
    }

    public string? InputPath { get; }

    public ImmutableArray<string> PromptPaths { get; }

    internal static LaunchOptions Empty { get; } = new(null, []);

    public static LaunchOptions Parse(string[] args) =>
        Parse(args, Directory.GetCurrentDirectory());

    public static LaunchOptions Parse(
        IEnumerable<string> args,
        string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        string canonicalCurrentDirectory;
        try
        {
            canonicalCurrentDirectory = Path.GetFullPath(currentDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new LaunchOptionsException(LaunchErrorCodes.InvalidPath, "CurrentDirectory");
        }

        string[] values = args.ToArray();
        string? inputPath = null;
        ImmutableArray<string>.Builder prompts = ImmutableArray.CreateBuilder<string>();
        HashSet<string> promptPaths = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        for (int index = 0; index < values.Length; index++)
        {
            string option = values[index] ?? string.Empty;
            bool isInput = string.Equals(option, "--input", StringComparison.OrdinalIgnoreCase);
            bool isPrompt = string.Equals(option, "--prompt", StringComparison.OrdinalIgnoreCase);
            if (!isInput && !isPrompt)
            {
                throw new LaunchOptionsException(LaunchErrorCodes.UnknownOption, "Option");
            }

            if (index + 1 >= values.Length
                || string.IsNullOrWhiteSpace(values[index + 1])
                || values[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new LaunchOptionsException(LaunchErrorCodes.MissingValue, option);
            }

            string path = CanonicalPath(values[++index], canonicalCurrentDirectory, option);
            if (isInput)
            {
                if (inputPath is not null)
                {
                    throw new LaunchOptionsException(LaunchErrorCodes.DuplicateInput, option);
                }

                inputPath = path;
                continue;
            }

            if (!string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
            {
                throw new LaunchOptionsException(LaunchErrorCodes.PromptExtensionInvalid, option);
            }

            if (promptPaths.Add(path))
            {
                prompts.Add(path);
            }
        }

        return new LaunchOptions(inputPath, prompts.ToImmutable());
    }

    public override string ToString() =>
        $"{nameof(LaunchOptions)} {{ HasInput = {InputPath is not null}, PromptCount = {PromptPaths.Length}, Content = <redacted> }}";

    private static string CanonicalPath(
        string path,
        string currentDirectory,
        string field)
    {
        try
        {
            return Path.GetFullPath(path, currentDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new LaunchOptionsException(LaunchErrorCodes.InvalidPath, field);
        }
    }
}

public sealed record ImportedPrompt
{
    public required string Path { get; init; }

    public required string DisplayName { get; init; }

    public required string Content { get; init; }

    public override string ToString() =>
        $"{nameof(ImportedPrompt)} {{ DisplayName = {DisplayName}, Content = <redacted> }}";
}

public sealed class PromptFileLoader
{
    public const int MaximumCharacters = 32_767;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public ImmutableArray<ImportedPrompt> Load(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ImmutableArray<ImportedPrompt>.Builder prompts =
            ImmutableArray.CreateBuilder<ImportedPrompt>(options.PromptPaths.Length);
        foreach (string path in options.PromptPaths)
        {
            prompts.Add(LoadOne(path));
        }

        return prompts.ToImmutable();
    }

    public ImportedPrompt LoadOne(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new LaunchOptionsException(LaunchErrorCodes.InvalidPath, "Prompt");
        }

        if (!string.Equals(Path.GetExtension(canonicalPath), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new LaunchOptionsException(LaunchErrorCodes.PromptExtensionInvalid, "Prompt");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(canonicalPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            throw new LaunchOptionsException(LaunchErrorCodes.PromptReadFailed, "Prompt");
        }

        int offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            ? Encoding.UTF8.Preamble.Length
            : 0;
        string content;
        try
        {
            content = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            throw new LaunchOptionsException(LaunchErrorCodes.PromptUtf8Invalid, "Prompt");
        }

        if (content.Length is < 1 or > MaximumCharacters)
        {
            throw new LaunchOptionsException(LaunchErrorCodes.PromptLengthInvalid, "Prompt");
        }

        return new ImportedPrompt
        {
            Path = canonicalPath,
            DisplayName = Path.GetFileName(canonicalPath),
            Content = content,
        };
    }

    public override string ToString() =>
        $"{nameof(PromptFileLoader)} {{ Content = <redacted> }}";
}

public sealed class LaunchStartupState
{
    private LaunchStartupState(
        LaunchOptions? options,
        ImmutableArray<ImportedPrompt> prompts,
        LaunchOptionsException? error)
    {
        Options = options;
        Prompts = prompts;
        Error = error;
    }

    public LaunchOptions? Options { get; }

    public ImmutableArray<ImportedPrompt> Prompts { get; }

    public LaunchOptionsException? Error { get; }

    public bool IsValid => Error is null && Options is not null;

    public static LaunchStartupState Create(
        string[] args,
        string? currentDirectory = null)
    {
        try
        {
            LaunchOptions options = currentDirectory is null
                ? LaunchOptions.Parse(args)
                : LaunchOptions.Parse(args, currentDirectory);
            return new LaunchStartupState(
                options,
                new PromptFileLoader().Load(options),
                null);
        }
        catch (LaunchOptionsException error)
        {
            return new LaunchStartupState(null, [], error);
        }
    }

    public static LaunchStartupState Empty { get; } =
        new(LaunchOptions.Empty, [], null);

    public override string ToString() =>
        $"{nameof(LaunchStartupState)} {{ IsValid = {IsValid}, PromptCount = {Prompts.Length}, ErrorCode = {Error?.Code ?? "<none>"}, Content = <redacted> }}";

}

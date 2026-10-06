using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitHub.Copilot;

namespace StudyReportEvaluator.App.Copilot;

public interface ICopilotCliPathResolver
{
    ValueTask<string?> ResolveAsync(CancellationToken cancellationToken);
}

public sealed class BundledCopilotCliPathResolver : ICopilotCliPathResolver
{
    public const string ManifestFileName = "copilot-runtime.json";
    internal const int CliHashBufferSize = 1 << 20;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string applicationBaseDirectory;

    public BundledCopilotCliPathResolver()
        : this(AppContext.BaseDirectory)
    {
    }

    public BundledCopilotCliPathResolver(string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        if (!Path.IsPathFullyQualified(applicationBaseDirectory))
        {
            throw new ArgumentException("The application base directory must be absolute.", nameof(applicationBaseDirectory));
        }

        this.applicationBaseDirectory = Path.GetFullPath(applicationBaseDirectory);
    }

    public async ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string manifestPath = Path.Combine(applicationBaseDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        FileInfo manifestInfo = new(manifestPath);
        manifestInfo.Refresh();
        if (!manifestInfo.Exists || manifestInfo.Length is <= 0 or > 16_384)
        {
            throw new InvalidDataException("The bundled Copilot manifest size is invalid.");
        }

        BundledCopilotManifest manifest;
        await using (FileStream manifestStream = new(
            manifestPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            }))
        {
            using StreamReader reader = new(
                manifestStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: true);
            string manifestJson = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (reader.CurrentEncoding.CodePage != Encoding.UTF8.CodePage)
            {
                throw new InvalidDataException("The bundled Copilot manifest must use UTF-8.");
            }

            manifest = JsonSerializer.Deserialize<BundledCopilotManifest>(
                manifestJson,
                ManifestJsonOptions)
                ?? throw new InvalidDataException("The bundled Copilot manifest is empty.");
        }

        string expectedRuntimeIdentifier = GetCurrentRuntimeIdentifier()
            ?? throw new PlatformNotSupportedException("The current platform has no bundled Copilot runtime.");
        string expectedRelativePath = $"runtimes/{expectedRuntimeIdentifier}/native/"
            + (OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        string sdkPackageVersion = GetSdkPackageVersion();
        if (manifest.SchemaVersion != 1
            || !string.Equals(manifest.RuntimeIdentifier, expectedRuntimeIdentifier, StringComparison.Ordinal)
            || !string.Equals(manifest.CliRelativePath, expectedRelativePath, StringComparison.Ordinal)
            || !string.Equals(manifest.SdkVersion, sdkPackageVersion, StringComparison.Ordinal)
            || !CopilotRuntimeIdentity.IsSafeVersion(manifest.CliVersion)
            || manifest.CliSha256.Length != 64
            || manifest.CliSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("The bundled Copilot manifest is invalid.");
        }

        string cliPath = Path.GetFullPath(
            Path.Combine(
                applicationBaseDirectory,
                manifest.CliRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relativeToBase = Path.GetRelativePath(applicationBaseDirectory, cliPath);
        if (Path.IsPathFullyQualified(relativeToBase)
            || relativeToBase == ".."
            || relativeToBase.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The bundled Copilot path escapes the application directory.");
        }

        FileInfo before = new(cliPath);
        before.Refresh();
        if (!before.Exists || before.Length == 0)
        {
            return null;
        }

        byte[] hash;
        await using (FileStream cliStream = new(
            cliPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                // The default 4 KiB async reads make hashing the large CLI take seconds per call.
                BufferSize = CliHashBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            }))
        {
            hash = await SHA256.HashDataAsync(cliStream, cancellationToken).ConfigureAwait(false);
        }

        FileInfo after = new(cliPath);
        after.Refresh();
        string? actualVersion = ReadSafeFileVersion(cliPath);
        if (!after.Exists
            || before.Length != after.Length
            || before.LastWriteTimeUtc != after.LastWriteTimeUtc
            || !string.Equals(Convert.ToHexString(hash), manifest.CliSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(actualVersion, manifest.CliVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The bundled Copilot runtime does not match its manifest.");
        }

        return cliPath;
    }

    public override string ToString() =>
        $"{nameof(BundledCopilotCliPathResolver)} {{ Content = <redacted> }}";

    private static string? GetCurrentRuntimeIdentifier()
    {
        string? os = OperatingSystem.IsWindows() ? "win" : null;
        string? architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return os is null || architecture is null ? null : $"{os}-{architecture}";
    }

    private static string? ReadSafeFileVersion(string path)
    {
        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(path);
        foreach (string? candidate in new[] { versionInfo.ProductVersion, versionInfo.FileVersion })
        {
            if (candidate is not null && CopilotRuntimeIdentity.IsSafeVersion(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetSdkPackageVersion()
    {
        string informationalVersion = typeof(CopilotClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? throw new InvalidDataException("The Copilot SDK informational version cannot be verified.");
        string packageVersion = informationalVersion.Split('+', 2)[0];
        return CopilotRuntimeIdentity.IsSafeVersion(packageVersion)
            ? packageVersion
            : throw new InvalidDataException("The Copilot SDK package version cannot be verified.");
    }

    private sealed record BundledCopilotManifest
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("runtimeIdentifier")]
        public required string RuntimeIdentifier { get; init; }

        [JsonPropertyName("cliVersion")]
        public required string CliVersion { get; init; }

        [JsonPropertyName("cliSha256")]
        public required string CliSha256 { get; init; }

        [JsonPropertyName("sdkVersion")]
        public required string SdkVersion { get; init; }

        [JsonPropertyName("cliRelativePath")]
        public required string CliRelativePath { get; init; }
    }
}

public interface ICopilotClientFactory
{
    ValueTask<CopilotClientCreationResult> CreateAsync(CancellationToken cancellationToken);
}

public enum CopilotClientCreationStatus
{
    Created,
    CliUnavailable,
    RuntimeFailed,
    Cancelled,
}

public sealed class CopilotRuntimeIdentity
{
    public CopilotRuntimeIdentity(
        string cliPath,
        string cliVersion,
        string cliSha256,
        string sdkInformationalVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkInformationalVersion);

        if (!Path.IsPathFullyQualified(cliPath))
        {
            throw new ArgumentException("The CLI path must be absolute.", nameof(cliPath));
        }

        if (!IsSafeVersion(cliVersion))
        {
            throw new ArgumentException("The CLI version is not a safe identity value.", nameof(cliVersion));
        }

        if (cliSha256.Length != 64 || cliSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The CLI SHA-256 must contain exactly 64 hexadecimal characters.", nameof(cliSha256));
        }

        if (!IsSafeVersion(sdkInformationalVersion))
        {
            throw new ArgumentException("The SDK informational version is not a safe identity value.", nameof(sdkInformationalVersion));
        }

        CliPath = Path.GetFullPath(cliPath);
        CliVersion = cliVersion;
        CliSha256 = cliSha256.ToUpperInvariant();
        SdkInformationalVersion = sdkInformationalVersion;
    }

    public string CliPath { get; }

    public string CliVersion { get; }

    public string CliSha256 { get; }

    public string SdkInformationalVersion { get; }

    public override string ToString() =>
        $"CLI version {CliVersion}, CLI SHA-256 {CliSha256}, SDK {SdkInformationalVersion}";

    internal static bool IsSafeVersion(string value) =>
        value.Length is > 0 and <= 128
        && char.IsAsciiDigit(value[0])
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '+' or '_');
}

public sealed class CopilotClientCreationResult
{
    private CopilotClientCreationResult(
        CopilotClientCreationStatus status,
        CopilotClient? client,
        CopilotClientOptions? options,
        CopilotRuntimeIdentity? identity)
    {
        Status = status;
        Client = client;
        Options = options;
        Identity = identity;
    }

    public CopilotClientCreationStatus Status { get; }

    public CopilotClient? Client { get; }

    public CopilotClientOptions? Options { get; }

    public CopilotRuntimeIdentity? Identity { get; }

    public override string ToString() =>
        Identity is null ? Status.ToString() : $"{Status}: {Identity}";

    internal static CopilotClientCreationResult Created(
        CopilotClient client,
        CopilotClientOptions options,
        CopilotRuntimeIdentity identity) =>
        new(CopilotClientCreationStatus.Created, client, options, identity);

    internal static CopilotClientCreationResult Failed(CopilotClientCreationStatus status)
    {
        if (status == CopilotClientCreationStatus.Created)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new CopilotClientCreationResult(status, null, null, null);
    }
}

public sealed class CopilotClientFactory : ICopilotClientFactory
{
    private readonly ICopilotCliPathResolver _pathResolver;

    public CopilotClientFactory()
        : this(new BundledCopilotCliPathResolver())
    {
    }

    public CopilotClientFactory(ICopilotCliPathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _pathResolver = pathResolver;
    }

    public async ValueTask<CopilotClientCreationResult> CreateAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.Cancelled);
        }

        string? resolvedPath;
        try
        {
            resolvedPath = await _pathResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.Cancelled);
        }
        catch
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.RuntimeFailed);
        }

        if (string.IsNullOrWhiteSpace(resolvedPath) || !Path.IsPathFullyQualified(resolvedPath))
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.CliUnavailable);
        }

        CopilotRuntimeIdentity identity;
        try
        {
            identity = await ReadIdentityAsync(Path.GetFullPath(resolvedPath), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.Cancelled);
        }
        catch (Exception exception) when (IsCliUnavailableException(exception))
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.CliUnavailable);
        }
        catch
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.RuntimeFailed);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.Cancelled);
        }

        CopilotClientOptions options = BuildOptions(identity.CliPath);
        try
        {
            CopilotClient client = new(options);
            return CopilotClientCreationResult.Created(client, options, identity);
        }
        catch
        {
            return CopilotClientCreationResult.Failed(CopilotClientCreationStatus.RuntimeFailed);
        }
    }

    private static CopilotClientOptions BuildOptions(string cliPath) =>
        new()
        {
            Connection = RuntimeConnection.ForStdio(path: cliPath),
            Mode = CopilotClientMode.CopilotCli,
            UseLoggedInUser = true,
            GitHubToken = null,
            LogLevel = CopilotLogLevel.None,
            Logger = null,
            Telemetry = null,
            OnListModels = null,
            EnableRemoteSessions = false,
        };

    private static async ValueTask<CopilotRuntimeIdentity> ReadIdentityAsync(
        string cliPath,
        CancellationToken cancellationToken)
    {
        FileInfo before = new(cliPath);
        before.Refresh();
        if (!before.Exists || before.Length == 0)
        {
            throw new FileNotFoundException("The Copilot CLI executable is unavailable.");
        }

        string? cliVersion = ReadCliVersion(cliPath);
        if (cliVersion is null)
        {
            throw new InvalidDataException("The Copilot CLI version cannot be verified.");
        }

        byte[] hash;
        await using (FileStream stream = new(
            cliPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = BundledCopilotCliPathResolver.CliHashBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            }))
        {
            hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        FileInfo after = new(cliPath);
        after.Refresh();
        if (!after.Exists
            || before.Length != after.Length
            || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
            throw new IOException("The Copilot CLI executable changed while its identity was being read.");
        }

        string? sdkInformationalVersion = typeof(CopilotClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (sdkInformationalVersion is null || !CopilotRuntimeIdentity.IsSafeVersion(sdkInformationalVersion))
        {
            throw new InvalidDataException("The Copilot SDK informational version cannot be verified.");
        }

        return new CopilotRuntimeIdentity(
            cliPath,
            cliVersion,
            Convert.ToHexString(hash),
            sdkInformationalVersion);
    }

    private static string? ReadCliVersion(string cliPath)
    {
        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(cliPath);
        foreach (string? candidate in new[] { versionInfo.ProductVersion, versionInfo.FileVersion })
        {
            if (candidate is not null && CopilotRuntimeIdentity.IsSafeVersion(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsCliUnavailableException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or Win32Exception
            or ArgumentException
            or NotSupportedException;
}

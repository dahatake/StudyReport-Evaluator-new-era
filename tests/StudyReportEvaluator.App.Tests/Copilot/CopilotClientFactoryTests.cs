using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-057 (AC-058)
public sealed class CopilotClientFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bundled_resolver_accepts_only_the_current_rid_manifest_and_matching_binary(
        bool writeUtf8Bom)
    {
        using TemporaryBundledCli bundle = TemporaryBundledCli.Create(writeUtf8Bom: writeUtf8Bom);
        BundledCopilotCliPathResolver resolver = new(bundle.Directory);

        string? result = await resolver.ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(bundle.CliPath, result);
        Assert.DoesNotContain(bundle.Directory, resolver.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bundled_resolver_hash_mismatch_maps_to_runtime_failure()
    {
        using TemporaryBundledCli bundle = TemporaryBundledCli.Create(cliSha256: new string('0', 64));
        CopilotClientFactory factory = new(new BundledCopilotCliPathResolver(bundle.Directory));

        CopilotClientCreationResult result = await factory.CreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotClientCreationStatus.RuntimeFailed, result.Status);
        Assert.Null(result.Client);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task Bundled_resolver_rejects_a_manifest_for_a_different_runtime_identifier()
    {
        string wrongRuntimeIdentifier = OperatingSystem.IsWindows()
            ? "linux-x64"
            : "win-x64";
        using TemporaryBundledCli bundle = TemporaryBundledCli.Create(
            runtimeIdentifier: wrongRuntimeIdentifier);
        BundledCopilotCliPathResolver resolver = new(bundle.Directory);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await resolver.ResolveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_bundled_manifest_does_not_fall_back_to_path()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-A01-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string pathNamedCli = Path.Combine(directory, OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
            File.Copy(typeof(CopilotClientFactoryTests).Assembly.Location, pathNamedCli);
            CopilotClientFactory factory = new(new BundledCopilotCliPathResolver(directory));

            CopilotClientCreationResult result = await factory.CreateAsync(TestContext.Current.CancellationToken);

            Assert.Equal(CopilotClientCreationStatus.CliUnavailable, result.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Default_factory_uses_only_the_bundled_runtime_resolver()
    {
        CopilotClientFactory factory = new();
        FieldInfo field = typeof(CopilotClientFactory).GetField(
            "_pathResolver",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The path resolver field was not found.");

        Assert.IsType<BundledCopilotCliPathResolver>(field.GetValue(factory));
    }

    [Fact]
    public async Task Factory_uses_explicit_cli_path_existing_login_and_no_secret_or_telemetry()
    {
        string cliPath = typeof(CopilotClientFactoryTests).Assembly.Location;
        CopilotClientFactory factory = new(new StubPathResolver(cliPath));

        CopilotClientCreationResult result = await factory.CreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotClientCreationStatus.Created, result.Status);
        CopilotClient client = Assert.IsType<CopilotClient>(result.Client);
        CopilotClientOptions options = Assert.IsType<CopilotClientOptions>(result.Options);
        StdioRuntimeConnection connection = Assert.IsType<StdioRuntimeConnection>(options.Connection);
        Assert.Equal(Path.GetFullPath(cliPath), connection.Path);
        Assert.True(connection.Args is null or { Count: 0 });
        Assert.Equal(CopilotClientMode.CopilotCli, options.Mode);
        Assert.True(options.UseLoggedInUser);
        Assert.Null(options.GitHubToken);
        Assert.Equal(CopilotLogLevel.None, options.LogLevel);
        Assert.Null(options.Logger);
        Assert.Null(options.Telemetry);
        Assert.Null(options.Environment);
        Assert.Null(GetOptionValue(options, "OnGitHubTelemetry"));
        Assert.Null(GetOptionValue(options, "RequestHandler"));
        Assert.Null(options.OnListModels);
        Assert.False(options.EnableRemoteSessions);
        Assert.Null(connection.Environment);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task Factory_returns_exact_cli_and_sdk_identity_without_exposing_path_in_safe_text()
    {
        string cliPath = typeof(CopilotClientFactoryTests).Assembly.Location;
        CopilotClientFactory factory = new(new StubPathResolver(cliPath));
        string expectedHash;
        await using (FileStream stream = File.OpenRead(cliPath))
        {
            expectedHash = Convert.ToHexString(await SHA256.HashDataAsync(
                stream,
                TestContext.Current.CancellationToken));
        }

        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(cliPath);
        string expectedCliVersion = versionInfo.ProductVersion ?? versionInfo.FileVersion
            ?? throw new InvalidOperationException("The test assembly has no file version.");
        string expectedSdkVersion = typeof(CopilotClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? throw new InvalidOperationException("The SDK has no informational version.");

        CopilotClientCreationResult result = await factory.CreateAsync(TestContext.Current.CancellationToken);

        CopilotRuntimeIdentity identity = Assert.IsType<CopilotRuntimeIdentity>(result.Identity);
        Assert.Equal(Path.GetFullPath(cliPath), identity.CliPath);
        Assert.Equal(expectedCliVersion, identity.CliVersion);
        Assert.Equal(expectedHash, identity.CliSha256);
        Assert.Equal(expectedSdkVersion, identity.SdkInformationalVersion);
        Assert.DoesNotContain(cliPath, identity.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cliPath, result.ToString(), StringComparison.OrdinalIgnoreCase);

        CopilotClient client = Assert.IsType<CopilotClient>(result.Client);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Missing_cli_maps_to_unavailable_without_constructing_a_bundled_client()
    {
        CopilotClientFactory factory = new(new StubPathResolver(null));

        CopilotClientCreationResult result = await factory.CreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotClientCreationStatus.CliUnavailable, result.Status);
        Assert.Null(result.Client);
        Assert.Null(result.Options);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task Relative_cli_path_is_rejected_instead_of_using_process_working_directory()
    {
        CopilotClientFactory factory = new(new StubPathResolver("copilot.exe"));

        CopilotClientCreationResult result = await factory.CreateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotClientCreationStatus.CliUnavailable, result.Status);
        Assert.Null(result.Client);
    }

    [Fact]
    public async Task Cancellation_is_closed_and_does_not_attempt_path_discovery()
    {
        StubPathResolver resolver = new(typeof(CopilotClientFactoryTests).Assembly.Location);
        CopilotClientFactory factory = new(resolver);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        CopilotClientCreationResult result = await factory.CreateAsync(cancellation.Token);

        Assert.Equal(CopilotClientCreationStatus.Cancelled, result.Status);
        Assert.False(resolver.WasCalled);
        Assert.Null(result.Client);
    }

    [Fact]
    public void Factory_public_api_has_no_oauth_pat_token_or_secret_input()
    {
        string[] parameterNames = typeof(CopilotClientFactory)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Concat(typeof(CopilotClientFactory)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters()))
            .Select(parameter => parameter.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(parameterNames, name =>
            name.Contains("oauth", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pat", StringComparison.OrdinalIgnoreCase)
            || name.Contains("personalAccessToken", StringComparison.OrdinalIgnoreCase)
            || name.Contains("githubToken", StringComparison.OrdinalIgnoreCase)
            || name.Contains("accessToken", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("C:\\private\\copilot.exe")]
    [InlineData("1.2.3\nforged")]
    [InlineData("release candidate")]
    public void Runtime_identity_rejects_unsafe_version_metadata(string unsafeVersion)
    {
        Assert.Throws<ArgumentException>(() => new CopilotRuntimeIdentity(
            Path.GetFullPath(typeof(CopilotClientFactoryTests).Assembly.Location),
            unsafeVersion,
            new string('A', 64),
            "1.0.11+test"));
    }

    private static object? GetOptionValue(CopilotClientOptions options, string propertyName) =>
        typeof(CopilotClientOptions).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(options);

    private sealed class StubPathResolver(string? path) : ICopilotCliPathResolver
    {
        public bool WasCalled { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(path);
        }
    }

    private sealed class TemporaryBundledCli : IDisposable
    {
        private TemporaryBundledCli(string directory, string cliPath)
        {
            Directory = directory;
            CliPath = cliPath;
        }

        public string Directory { get; }

        public string CliPath { get; }

        public static TemporaryBundledCli Create(
            string? cliSha256 = null,
            bool writeUtf8Bom = false,
            string? runtimeIdentifier = null)
        {
            runtimeIdentifier ??= CurrentRuntimeIdentifier();
            string binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
            string directory = Path.Combine(
                Path.GetTempPath(),
                "StudyReportEvaluator-A01-" + Guid.NewGuid().ToString("N"));
            string nativeDirectory = Path.Combine(directory, "runtimes", runtimeIdentifier, "native");
            DirectoryInfo created = System.IO.Directory.CreateDirectory(nativeDirectory);
            string cliPath = Path.Combine(created.FullName, binaryName);
            File.Copy(typeof(CopilotClientFactoryTests).Assembly.Location, cliPath);

            string actualHash;
            using (FileStream stream = File.OpenRead(cliPath))
            {
                actualHash = Convert.ToHexString(SHA256.HashData(stream));
            }

            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(cliPath);
            string cliVersion = versionInfo.ProductVersion ?? versionInfo.FileVersion
                ?? throw new InvalidOperationException("The test binary has no version.");
            string sdkVersion = typeof(CopilotClient).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? throw new InvalidOperationException("The SDK has no informational version.");
            sdkVersion = sdkVersion.Split('+', 2)[0];
            string manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                runtimeIdentifier,
                cliVersion,
                cliSha256 = cliSha256 ?? actualHash,
                sdkVersion,
                cliRelativePath = $"runtimes/{runtimeIdentifier}/native/{binaryName}",
            });
            string manifestPath = Path.Combine(directory, BundledCopilotCliPathResolver.ManifestFileName);
            if (writeUtf8Bom)
            {
                File.WriteAllBytes(
                    manifestPath,
                    [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(manifest)]);
            }
            else
            {
                File.WriteAllText(manifestPath, manifest);
            }
            return new TemporaryBundledCli(directory, cliPath);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }

        private static string CurrentRuntimeIdentifier()
        {
            string os = OperatingSystem.IsWindows() ? "win" : "osx";
            string architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(),
            };
            return $"{os}-{architecture}";
        }
    }
}

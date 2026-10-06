using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

// Requirements: NFR-OPS-001 (AC-074)
public sealed class WindowsLocalApplicationTests
{
    private static readonly string[] ForbiddenDependencyMarkers =
    [
        "Microsoft.Office",
        "Office.Interop",
        "LibreOffice",
        "soffice",
    ];

    private static readonly string[] ForbiddenSourceMarkers =
    [
        "Microsoft.Office.Interop",
        "Excel.Application",
        "Type.GetTypeFromProgID",
        "Marshal.GetActiveObject",
        "LibreOffice",
        "soffice",
    ];

    [Fact]
    public async Task Normal_release_framework_dependent_app_starts_without_office_or_com_runtime()
    {
        E02RepositoryLayout.RequireWindowsX64();
        string repositoryRoot = E02RepositoryLayout.FindRepositoryRoot();
        string outputDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "StudyReportEvaluator.App",
            "bin",
            "Release",
            "net10.0");
        string executablePath = Path.Combine(outputDirectory, "StudyReportEvaluator.App.exe");
        string applicationPath = Path.Combine(outputDirectory, "StudyReportEvaluator.App.dll");
        string runtimeConfigPath = Path.Combine(
            outputDirectory,
            "StudyReportEvaluator.App.runtimeconfig.json");
        string dependencyContextPath = Path.Combine(
            outputDirectory,
            "StudyReportEvaluator.App.deps.json");

        Assert.True(
            Directory.Exists(outputDirectory),
            "The normal Release GATE-APP build output is required before E-02 runs.");
        Assert.True(File.Exists(executablePath));
        Assert.True(File.Exists(applicationPath));
        Assert.True(File.Exists(runtimeConfigPath));
        Assert.True(File.Exists(dependencyContextPath));
        Assert.DoesNotContain(
            outputDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            segment => string.Equals(segment, "publish", StringComparison.OrdinalIgnoreCase));

        AssertFrameworkDependentRuntime(runtimeConfigPath, dependencyContextPath, outputDirectory);
        AssertAmd64AppHost(executablePath);
        AssertNoOfficeOrComSourceDependency(repositoryRoot);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = outputDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
            },
        };

        try
        {
            Assert.True(process.Start());
            bool exitedDuringStartupProbe = await WaitForExitAsync(
                process,
                TimeSpan.FromMilliseconds(1_500),
                TestContext.Current.CancellationToken);
            Assert.False(
                exitedDuringStartupProbe,
                exitedDuringStartupProbe
                    ? $"The application exited during its startup probe with code {process.ExitCode}."
                    : "The application must remain alive during its startup probe.");
            process.Refresh();
            Assert.False(process.HasExited);
            Assert.True(process.Id > 0);

            string[] forbiddenModules = process.Modules
                .Cast<ProcessModule>()
                .Select(module => module.ModuleName)
                .Where(IsOfficeRuntimeModule)
                .ToArray();
            Assert.Empty(forbiddenModules);

            if (process.CloseMainWindow()
                && await WaitForExitAsync(
                    process,
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken))
            {
                Assert.Equal(0, process.ExitCode);
            }
        }
        finally
        {
            await EnsureStoppedAsync(process);
        }
    }

    private static void AssertFrameworkDependentRuntime(
        string runtimeConfigPath,
        string dependencyContextPath,
        string outputDirectory)
    {
        using JsonDocument runtimeConfig = JsonDocument.Parse(
            File.ReadAllBytes(runtimeConfigPath));
        JsonElement runtimeOptions = runtimeConfig.RootElement.GetProperty("runtimeOptions");
        Assert.Equal("net10.0", runtimeOptions.GetProperty("tfm").GetString());
        Assert.False(runtimeOptions.TryGetProperty("includedFrameworks", out _));
        JsonElement framework = runtimeOptions.GetProperty("framework");
        Assert.Equal("Microsoft.NETCore.App", framework.GetProperty("name").GetString());
        Assert.StartsWith(
            "10.0.",
            framework.GetProperty("version").GetString(),
            StringComparison.Ordinal);

        using JsonDocument dependencyContext = JsonDocument.Parse(
            File.ReadAllBytes(dependencyContextPath));
        Assert.Equal(
            ".NETCoreApp,Version=v10.0",
            dependencyContext.RootElement
                .GetProperty("runtimeTarget")
                .GetProperty("name")
                .GetString());
        string[] forbiddenDependencies = dependencyContext.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Select(library => library.Name)
            .Where(name => ForbiddenDependencyMarkers.Any(marker =>
                name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.Empty(forbiddenDependencies);

        Assert.False(File.Exists(Path.Combine(outputDirectory, "coreclr.dll")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "hostfxr.dll")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "hostpolicy.dll")));
    }

    private static void AssertAmd64AppHost(string executablePath)
    {
        using FileStream stream = new(
            executablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using PEReader reader = new(stream);
        Assert.Equal(Machine.Amd64, reader.PEHeaders.CoffHeader.Machine);
    }

    private static void AssertNoOfficeOrComSourceDependency(string repositoryRoot)
    {
        string sourceRoot = Path.Combine(repositoryRoot, "src", "StudyReportEvaluator.App");
        foreach (string sourceFile in Directory.EnumerateFiles(
                     sourceRoot,
                     "*.cs",
                     SearchOption.AllDirectories)
                 .Where(path => !IsBuildOutput(path)))
        {
            string source = File.ReadAllText(sourceFile);
            bool containsForbiddenMarker = ForbiddenSourceMarkers.Any(marker =>
                source.Contains(marker, StringComparison.OrdinalIgnoreCase));
            Assert.False(
                containsForbiddenMarker,
                "Production source must not use Excel, Office, LibreOffice, or COM automation.");
        }
    }

    private static bool IsBuildOutput(string path)
    {
        string normalized = Path.GetFullPath(path).Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOfficeRuntimeModule(string moduleName)
    {
        string name = Path.GetFileNameWithoutExtension(moduleName);
        return string.Equals(name, "excel", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "soffice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("libreoffice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("office.interop", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.Office", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task EnsureStoppedAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            if (process.CloseMainWindow()
                && await WaitForExitAsync(
                    process,
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None))
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        bool exited = await WaitForExitAsync(
            process,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        Assert.True(exited, "The local application process did not terminate during test cleanup.");
    }
}

using Wry.NET.Bridge.Generator;
using Wry.NET.Bridge.Generator.Tests.Fixtures;

namespace Wry.NET.Bridge.Generator.Tests;

public class EndToEndTests : IDisposable
{
    private readonly string _outputDir;

    public EndToEndTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"BindingGenE2E_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

    #region Argument Validation

    [Fact]
    public void Main_MissingArgs_Returns1()
    {
        var result = Program.Main(Array.Empty<string>());
        Assert.Equal(1, result);
    }

    [Fact]
    public void Main_MissingAssembly_Returns1()
    {
        var result = Program.Main(new[] { "nonexistent.dll", _outputDir });
        Assert.Equal(1, result);
    }

    #endregion

    #region Full Pipeline

    [Fact]
    public void Main_WithTestAssembly_ReturnsZero()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        var result = Program.Main(new[] { assemblyPath, _outputDir });

        Assert.Equal(0, result);
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesServiceFiles()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        Assert.True(File.Exists(Path.Combine(_outputDir, "BasicService.ts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "AsyncService.ts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "ModelService.ts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "CancellationService.ts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "IgnoredMethodService.ts")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "CallContextService.ts")));
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesCustomNamedService()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        // CustomNamedService has [BridgeService(Name = "CustomApi")]
        Assert.True(File.Exists(Path.Combine(_outputDir, "CustomApi.ts")));
        Assert.False(File.Exists(Path.Combine(_outputDir, "CustomNamedService.ts")));
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesModelsFile()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var modelsPath = Path.Combine(_outputDir, "models.ts");
        Assert.True(File.Exists(modelsPath));

        var content = File.ReadAllText(modelsPath);
        Assert.Contains("export interface SimpleModel", content);
        Assert.Contains("export interface DerivedModel extends BaseModel", content);
        Assert.Contains("export enum TestEnum", content);
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesEventsFile()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var eventsPath = Path.Combine(_outputDir, "events.ts");
        Assert.True(File.Exists(eventsPath));

        var content = File.ReadAllText(eventsPath);
        Assert.Contains("onTestEvent", content);
        Assert.Contains("onAnother", content);
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesIndexFile()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var indexPath = Path.Combine(_outputDir, "index.ts");
        Assert.True(File.Exists(indexPath));

        var content = File.ReadAllText(indexPath);
        Assert.Contains("export * as BasicService from \"./BasicService\";", content);
        Assert.Contains("export * from \"./models\";", content);
        Assert.Contains("export * from \"./events\";", content);
    }

    [Fact]
    public void Main_WithTestAssembly_ServiceFileContent_HasCorrectSignatures()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "BasicService.ts"));
        Assert.Contains("export function Greet(name: string): Promise<string>", content);
        Assert.Contains("export function Add(a: number, b: number): Promise<number>", content);
        Assert.Contains("export function DoNothing(): Promise<void>", content);
    }

    [Fact]
    public void Main_WithTestAssembly_ByteArrayMappedToString()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "ModelService.ts"));
        // byte[] should be mapped to string (base64)
        Assert.Contains("EchoBytes(data: string): Promise<string>", content);
    }

    [Fact]
    public void Main_WithTestAssembly_CancellationTokenStrippedFromSignature()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "CancellationService.ts"));
        // CancellationToken should be stripped — SlowMethod should only have 'seconds'
        Assert.Contains("SlowMethod(seconds: number)", content);
        Assert.DoesNotContain("CancellationToken", content);
        Assert.DoesNotContain("cancellationToken", content);
    }

    [Fact]
    public void Main_WithTestAssembly_IgnoredMethodExcluded()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "IgnoredMethodService.ts"));
        Assert.Contains("export function Visible()", content);
        Assert.DoesNotContain("Hidden", content);
    }

    [Fact]
    public void Main_WithTestAssembly_CallContextStrippedFromSignature()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "CallContextService.ts"));
        Assert.Contains("export function GetTitle(): Promise<string>", content);
        Assert.Contains("export function GetTitleWithArg(name: string): Promise<string>", content);
        Assert.DoesNotContain("ctx:", content);
        Assert.DoesNotContain("ctx)", content);
    }

    [Fact]
    public void Main_WithTestAssembly_JsonPropertyNameRespected()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "models.ts"));
        // JsonCustomModel.CustomName has [JsonPropertyName("custom_name")]
        Assert.Contains("custom_name: string;", content);
    }

    [Fact]
    public void Main_WithTestAssembly_JsonIgnoreRespected()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "models.ts"));
        // JsonCustomModel.Secret has [JsonIgnore], should not appear
        Assert.DoesNotContain("secret", content);
        Assert.DoesNotContain("Secret", content);
    }

    [Fact]
    public void Main_WithTestAssembly_CleansUpStaleFiles()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        // First run creates files
        Program.Main(new[] { assemblyPath, _outputDir });

        // Drop a fake stale file
        var staleFile = Path.Combine(_outputDir, "DeletedService.ts");
        File.WriteAllText(staleFile, "// stale");

        // Second run should clean it up
        Program.Main(new[] { assemblyPath, _outputDir });

        Assert.False(File.Exists(staleFile), "Stale file should be deleted on subsequent run");
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesEmptyAndAllIgnoredServiceFiles()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var emptyPath = Path.Combine(_outputDir, "EmptyService.ts");
        var allIgnoredPath = Path.Combine(_outputDir, "AllIgnoredService.ts");
        Assert.True(File.Exists(emptyPath));
        Assert.True(File.Exists(allIgnoredPath));

        var emptyContent = File.ReadAllText(emptyPath);
        Assert.Contains("// Auto-generated", emptyContent);
        Assert.Contains("import { call }", emptyContent);

        var allIgnoredContent = File.ReadAllText(allIgnoredPath);
        Assert.Contains("// Auto-generated", allIgnoredContent);
        Assert.DoesNotContain("Hidden1", allIgnoredContent);
        Assert.DoesNotContain("Hidden2", allIgnoredContent);
    }

    [Fact]
    public void Main_WithTestAssembly_GeneratesEmptyModelAndEdgeCaseEnum()
    {
        var assemblyPath = typeof(BasicService).Assembly.Location;

        Program.Main(new[] { assemblyPath, _outputDir });

        var content = File.ReadAllText(Path.Combine(_outputDir, "models.ts"));
        Assert.Contains("export interface EmptyModel {", content);
        Assert.Contains("export enum EdgeCaseEnum", content);
        Assert.Contains("Negative = -1", content);
        Assert.Contains("SameAsZero = 0", content);
    }

    #endregion
}

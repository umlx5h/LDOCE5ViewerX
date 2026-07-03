using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace LDOCE5ViewerXBenchmarks;

/// <summary>
/// Provides the default benchmark output configuration.
/// </summary>
public sealed class BenchmarkConfig : ManualConfig
{
    /// <summary>
    /// Creates the benchmark output configuration.
    /// </summary>
    public BenchmarkConfig()
    {
        WithOptions(ConfigOptions.DisableOptimizationsValidator);

        AddDiagnoser(MemoryDiagnoser.Default);
        AddJob(Job.ShortRun.WithWarmupCount(1).WithToolchain(InProcessEmitToolchain.Instance));
    }
}

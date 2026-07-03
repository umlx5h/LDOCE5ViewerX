using BenchmarkDotNet.Running;

using LDOCE5ViewerXBenchmarks.Benchmarks;

if (args.Length > 0)
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
else
{
    BenchmarkRunner.Run<FullTextSearcherBenchmarks>();
}

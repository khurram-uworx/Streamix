using BenchmarkDotNet.Attributes;
using Streamix;
using Streamix.Abstractions;

namespace Streamix.Benchmarks;

[MemoryDiagnoser]
public class CoreOperatorBenchmarks
{
    private const int ItemCount = 1000;
    private IStream<int> source = null!;

    [GlobalSetup]
    public void Setup()
    {
        source = Stream.Range(1, ItemCount);
    }

    [Benchmark]
    public async Task Map()
    {
        await source.Map(x => x * 2).ForEachAsync(_ => { });
    }

    [Benchmark]
    public async Task Filter()
    {
        await source.Filter(x => x % 2 == 0).ForEachAsync(_ => { });
    }

    [Benchmark]
    public async Task FlatMap_Sequential()
    {
        await source.FlatMap(async x =>
        {
            await Task.Yield();
            return x;
        }, maxConcurrency: 1).ForEachAsync(_ => { });
    }

    [Benchmark]
    public async Task FlatMap_Concurrent()
    {
        await source.FlatMap(async x =>
        {
            await Task.Yield();
            return x;
        }, maxConcurrency: 10).ForEachAsync(_ => { });
    }

    [Benchmark]
    public async Task Merge()
    {
        var s1 = Stream.Range(1, ItemCount / 2);
        var s2 = Stream.Range(ItemCount / 2, ItemCount / 2);
        await Stream.Merge(s1, s2).ForEachAsync(_ => { });
    }

    [Benchmark]
    public async Task Buffer()
    {
        await source.Buffer(100).ForEachAsync(_ => { });
    }

    [Benchmark]
    public async Task Timeout_NoTimeout()
    {
        await source.Timeout(TimeSpan.FromSeconds(10)).ForEachAsync(_ => { });
    }
}

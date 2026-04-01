using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Streamix;

namespace Streamix.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

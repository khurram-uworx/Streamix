using NUnit.Framework;

namespace Streamix.Tests;

/// <summary>
/// This test class ensures that the promised public API surface is available and compiles.
/// It uses reflection to verify members existence without executing them, as they are currently stubs.
/// </summary>
[TestFixture]
public class ApiContractTests
{
    static void assertDevxSurface(Type type, Type stringType)
    {
        Assert.That(type.GetMethod("Named", new[] { stringType }), Is.Not.Null, $"{type.Name} should expose Named(string).");
    }

    [Test]
    public void Stream_Contract_Surface_Area_Exists()
    {
        var type = typeof(IFlux<int>);

        // Factory methods on static facade
        Assert.That(typeof(Flux).GetMethod("Range"), Is.Not.Null);
        Assert.That(typeof(Flux).GetMethod("Empty"), Is.Not.Null);
        Assert.That(typeof(Flux).GetMethods().Any(m => m.Name == "From"), Is.True);
        Assert.That(typeof(Flux).GetMethods().Any(m => m.Name == "FromTask"), Is.True);
        Assert.That(typeof(Flux).GetMethods().Any(m => m.Name == "FromValueTask"), Is.True);
        Assert.That(typeof(Flux).GetMethod("FromEvent"), Is.Not.Null);
        Assert.That(typeof(Flux).GetMethod("FromTimer", new[] { typeof(TimeSpan) }), Is.Not.Null);
        Assert.That(typeof(Flux).GetMethods().Any(m => m.Name == "FromQueue"), Is.True);
        Assert.That(typeof(Flux).GetMethod("Merge"), Is.Not.Null);
        Assert.That(typeof(Flux).GetMethods().Any(m => m.Name == "Zip"), Is.True);

        // Instance methods on IStream
        Assert.That(type.GetMethod("ParallelMap"), Is.Null);
        Assert.That(type.GetMethod("ParallelMapOrdered"), Is.Null);
        Assert.That(type.GetMethod("FlatMapMany"), Is.Null);
        Assert.That(type.GetMethod("FlatMapManyAwait"), Is.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "Window"), Is.True);
        Assert.That(type.GetMethod("RunOn"), Is.Not.Null);
        assertDevxSurface(type, typeof(string));

        // Terminal extensions (LINQ style)
        var extensionsType = typeof(TerminalExtensions);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "FirstAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "FirstOrDefaultAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "LastAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "LastOrDefaultAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "SingleAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "SingleOrDefaultAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "CountAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "AnyAsync"), Is.True);
        Assert.That(extensionsType.GetMethods().Any(m => m.Name == "AllAsync"), Is.True);

        // Resilience extensions
        var resilienceType = typeof(ResilienceExtensions);
        Assert.That(resilienceType.GetMethods().Any(m => m.Name == "OnErrorReturnAsync"), Is.True);
        Assert.That(resilienceType.GetMethods().Any(m => m.Name == "RetryThenReturn"), Is.True);
        Assert.That(resilienceType.GetMethods().Any(m => m.Name == "RetryThenReturnAsync"), Is.True);
        Assert.That(resilienceType.GetMethods().Any(m => m.Name == "RetryThenResume"), Is.True);
    }

    [Test]
    public void Single_Contract_Surface_Area_Exists()
    {
        var type = typeof(ISingle<int>);

        // Factory methods on static facade
        Assert.That(typeof(Single).GetMethods().Any(m => m.Name == "From"), Is.True);
        Assert.That(typeof(Single).GetMethods().Any(m => m.Name == "FromTask"), Is.True);
        Assert.That(typeof(Single).GetMethods().Any(m => m.Name == "FromValueTask"), Is.True);

        // Instance methods on ISingle
        Assert.That(type.GetMethods().Any(m => m.Name == "Map"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "Select"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "FlatMap"), Is.True);
        Assert.That(type.GetMethod("OnErrorResume"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "OnErrorReturn"), Is.True);
        Assert.That(type.GetMethod("OnErrorMap"), Is.Not.Null);
        Assert.That(type.GetMethod("RunOn"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "ForEachAsync"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "Retry"), Is.True);
        Assert.That(type.GetMethod("ToTask"), Is.Not.Null);
        assertDevxSurface(type, typeof(string));
    }

    [Test]
    public void ConnectableStream_Contract_Surface_Area_Exists()
    {
        var type = typeof(IConnectableStream<int>);

        Assert.That(type.GetMethod("Connect"), Is.Not.Null);
        Assert.That(type.GetMethod("RefCount"), Is.Not.Null);
    }
}

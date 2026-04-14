using NUnit.Framework;
using System.Reflection;

namespace Streamix.Tests;

/// <summary>
/// This test class ensures that the promised public API surface is available and compiles.
/// It uses reflection to verify members existence without executing them, as they are currently stubs.
/// </summary>
[TestFixture]
public class ApiContractTests
{
    [Test]
    public void Stream_Contract_Surface_Area_Exists()
    {
        var type = typeof(IStream<int>);

        // Factory methods on static facade
        Assert.That(typeof(Stream).GetMethod("Range"), Is.Not.Null);
        Assert.That(typeof(Stream).GetMethod("Empty"), Is.Not.Null);
        Assert.That(typeof(Stream).GetMethods().Any(m => m.Name == "From"), Is.True);
        Assert.That(typeof(Stream).GetMethod("FromEvent"), Is.Not.Null);
        Assert.That(typeof(Stream).GetMethod("FromTimer", new[] { typeof(TimeSpan) }), Is.Not.Null);
        Assert.That(typeof(Stream).GetMethods().Any(m => m.Name == "FromQueue"), Is.True);
        Assert.That(typeof(Stream).GetMethod("Merge"), Is.Not.Null);
        Assert.That(typeof(Stream).GetMethods().Any(m => m.Name == "Zip"), Is.True);

        // Instance methods on IStream
        Assert.That(type.GetMethods().Any(m => m.Name == "Map"), Is.True);
        Assert.That(type.GetMethod("Filter"), Is.Not.Null);
        Assert.That(type.GetMethod("MapOrdered"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "FlatMap"), Is.True);
        Assert.That(type.GetMethod("ConcatMap"), Is.Not.Null);
        Assert.That(type.GetMethod("FlatMapOrdered"), Is.Not.Null);
        var flatMapOrdered = type.GetMethod("FlatMapOrdered");
        Assert.That(flatMapOrdered?.GetParameters().Length, Is.EqualTo(3));
        Assert.That(flatMapOrdered?.GetParameters()[1].HasDefaultValue, Is.True);
        Assert.That(flatMapOrdered?.GetParameters()[1].DefaultValue, Is.EqualTo(int.MaxValue));
        Assert.That(flatMapOrdered?.GetParameters()[2].HasDefaultValue, Is.True);
        Assert.That(flatMapOrdered?.GetParameters()[2].DefaultValue, Is.EqualTo(16));
        Assert.That(type.GetMethod("ParallelMap"), Is.Null);
        Assert.That(type.GetMethod("ParallelMapOrdered"), Is.Null);
        Assert.That(type.GetMethod("FlatMapMany"), Is.Null);
        Assert.That(type.GetMethod("FlatMapManyAwait"), Is.Null);
        Assert.That(type.GetMethod("Take"), Is.Not.Null);
        Assert.That(type.GetMethod("Skip"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "Buffer"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "Window"), Is.True);
        Assert.That(type.GetMethod("Throttle"), Is.Not.Null);
        Assert.That(type.GetMethod("Delay"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "Retry"), Is.True);
        Assert.That(type.GetMethod("Timeout"), Is.Not.Null);
        Assert.That(type.GetMethod("OnErrorResume"), Is.Not.Null);
        Assert.That(type.GetMethod("OnErrorReturn"), Is.Not.Null);
        Assert.That(type.GetMethod("OnErrorMap"), Is.Not.Null);
        Assert.That(type.GetMethod("Publish"), Is.Not.Null);
        Assert.That(type.GetMethod("RunOn"), Is.Not.Null);
        Assert.That(type.GetMethod("PipeThroughChannel"), Is.Not.Null);
        Assert.That(type.GetMethod("RunOnChannel"), Is.Not.Null);
        Assert.That(type.GetMethod("TeeToChannel"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "ForEachAsync"), Is.True);
        AssertDevxSurface(type, typeof(string));

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
    }

    [Test]
    public void Single_Contract_Surface_Area_Exists()
    {
        var type = typeof(ISingle<int>);

        // Factory methods on static facade
        Assert.That(typeof(Single).GetMethods().Any(m => m.Name == "From"), Is.True);

        // Instance methods on ISingle
        Assert.That(type.GetMethods().Any(m => m.Name == "Map"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "Select"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "FlatMap"), Is.True);
        Assert.That(type.GetMethod("OnErrorResume"), Is.Not.Null);
        Assert.That(type.GetMethod("OnErrorReturn"), Is.Not.Null);
        Assert.That(type.GetMethod("OnErrorMap"), Is.Not.Null);
        Assert.That(type.GetMethod("RunOn"), Is.Not.Null);
        Assert.That(type.GetMethods().Any(m => m.Name == "ForEachAsync"), Is.True);
        Assert.That(type.GetMethods().Any(m => m.Name == "Retry"), Is.True);
        Assert.That(type.GetMethod("ToTask"), Is.Not.Null);
        AssertDevxSurface(type, typeof(string));
    }

    [Test]
    public void ConnectableStream_Contract_Surface_Area_Exists()
    {
        var type = typeof(IConnectableStream<int>);

        Assert.That(type.GetMethod("Connect"), Is.Not.Null);
        Assert.That(type.GetMethod("RefCount"), Is.Not.Null);
    }

    private static void AssertDevxSurface(Type type, Type stringType)
    {
        Assert.That(type.GetMethod("Named", new[] { stringType }), Is.Not.Null, $"{type.Name} should expose Named(string).");
        Assert.That(type.GetMethod("Log", Type.EmptyTypes), Is.Not.Null, $"{type.Name} should expose Log().");
        Assert.That(type.GetMethod("Log", new[] { stringType }), Is.Not.Null, $"{type.Name} should expose Log(string).");

        var logWithLogger = type.GetMethods().SingleOrDefault(m =>
            m.Name == "Log" &&
            ParametersMatch(m, typeof(Microsoft.Extensions.Logging.ILogger), typeof(string)) &&
            m.GetParameters()[1].HasDefaultValue &&
            m.GetParameters()[1].DefaultValue is null);

        Assert.That(logWithLogger, Is.Not.Null, $"{type.Name} should expose Log(ILogger, string? prefix = null).");
        Assert.That(type.GetMethod("Debug", Type.EmptyTypes), Is.Not.Null, $"{type.Name} should expose Debug().");
        Assert.That(type.GetMethod("Debug", new[] { stringType }), Is.Not.Null, $"{type.Name} should expose Debug(string).");
        Assert.That(type.GetMethod("Checkpoint", new[] { stringType }), Is.Not.Null, $"{type.Name} should expose Checkpoint(string).");
        Assert.That(type.GetMethod("Checkpoint", new[] { stringType, typeof(Action<string>) }), Is.Not.Null, $"{type.Name} should expose Checkpoint(string, Action<string>).");
        Assert.That(type.GetMethod("Trace", Type.EmptyTypes), Is.Not.Null, $"{type.Name} should expose Trace().");
        Assert.That(type.GetMethod("Trace", new[] { stringType }), Is.Not.Null, $"{type.Name} should expose Trace(string).");

        var traceWithLogger = type.GetMethods().SingleOrDefault(m =>
            m.Name == "Trace" &&
            ParametersMatch(m, typeof(Microsoft.Extensions.Logging.ILogger), typeof(string)) &&
            m.GetParameters()[1].HasDefaultValue &&
            m.GetParameters()[1].DefaultValue is null);

        Assert.That(traceWithLogger, Is.Not.Null, $"{type.Name} should expose Trace(ILogger, string? prefix = null).");
    }

    private static bool ParametersMatch(MethodInfo method, params Type[] parameterTypes)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != parameterTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType != parameterTypes[i])
            {
                return false;
            }
        }

        return true;
    }
}

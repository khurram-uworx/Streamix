using NUnit.Framework;

namespace Streamix.Tests.Reactive;

[TestFixture]
public class IxTests
{
    [Test]
    public async Task Defer_Factory_Is_Not_Called_Until_Enumeration()
    {
        int factoryCalls = 0;
        var enumerable = EnumerableEx.Defer(() =>
        {
            factoryCalls++;
            return Enumerable.Range(1, 3);
        });

        Assert.That(factoryCalls, Is.EqualTo(0));

        _ = enumerable.ToArray();
        Assert.That(factoryCalls, Is.EqualTo(1));
    }
}

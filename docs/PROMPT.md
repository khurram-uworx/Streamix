Use README.md to understand the STREAMIX, our current repository

I want to make Streams Composable Across Boundaries, and for this we need to Add Creation Operators
I want you to focus on this only onwards; as this can be a critical gap

👉 Without rich sources, adoption stalls, we might need more ways to enter our system.

Today we might only rely on From(IAsyncEnumerable) but real-world systems are:
- event-driven
- callback-based
- polling-based

We should have things like:

Stream.From(Task<T>)
Stream.From(Func<Task<T>>)
Stream.Defer(() => ...)
Stream.Create(async (emitter, ct) => { ... })
Stream.Generate(...)
Stream.Interval(TimeSpan)

[src\Streamix\Stream.cs] and [src\Streamix\Single.cs] has few factory methods, lets create [docs\CREATION.md] file with a plan to cover this gap (if any)
Suggest what else we should have and then we will review and iterate on this plan

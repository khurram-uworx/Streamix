using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Streamix.Extensions;
using System.Collections.Concurrent;

namespace Streamix.Tests;

[TestFixture]
public class EfStreamTests
{
    class TestEntity
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    class TrackingDbContext : DbContext
    {
        private readonly Action<int> onDispose;

        public TrackingDbContext(DbContextOptions<TrackingDbContext> options, int instanceId, Action<int> onDispose)
            : base(options)
        {
            InstanceId = instanceId;
            this.onDispose = onDispose;
        }

        public int InstanceId { get; }

        public DbSet<TestEntity> Entities => Set<TestEntity>();

        public override async ValueTask DisposeAsync()
        {
            onDispose(InstanceId);
            await base.DisposeAsync();
        }
    }

    [Test]
    public async Task From_EmitsExpectedEntities()
    {
        var stream = EfStream.From(
            ctx => ctx.Set<TestEntity>()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Id),
            createSeededFactory(),
            name: "ActiveEntities");

        var result = await stream
            .Map(x => x.Name)
            .ToListAsync();

        Assert.That(result, Is.EqualTo(new[] { "A", "C" }));
    }

    [Test]
    public async Task From_Cancellation_ThrowsAndDisposesContext()
    {
        var disposedContextIds = new ConcurrentBag<int>();
        var stream = EfStream.From(
            ctx => ctx.Set<TestEntity>().OrderBy(x => x.Id),
            createSeededFactory(disposedContextIds));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in stream.WithCancellation(cts.Token))
            {
            }
        });

        Assert.That(disposedContextIds, Has.Count.EqualTo(1));
    }

    [Test]
    public void From_QueryBuilderFailure_PropagatesAndDisposesContext()
    {
        var disposedContextIds = new ConcurrentBag<int>();
        var stream = EfStream.From<TestEntity>(
            _ => throw new InvalidOperationException("query failed"),
            createSeededFactory(disposedContextIds));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await stream.ToListAsync());
        Assert.That(exception!.Message, Is.EqualTo("query failed"));
        Assert.That(disposedContextIds, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task From_CreatesDistinctContextPerSubscription()
    {
        var queryContextIds = new ConcurrentBag<int>();
        var disposedContextIds = new ConcurrentBag<int>();
        var stream = EfStream.From(
            ctx =>
            {
                var typed = (TrackingDbContext)ctx;
                queryContextIds.Add(typed.InstanceId);
                return typed.Entities.OrderBy(x => x.Id);
            },
            createSeededFactory(disposedContextIds));

        await stream.ToListAsync();
        await stream.ToListAsync();

        Assert.That(queryContextIds.Distinct().Count(), Is.EqualTo(2));
        Assert.That(disposedContextIds.Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task From_ComposesWithMapFilterTakeAndForEachAsync()
    {
        var stream = EfStream.From(
            ctx => ctx.Set<TestEntity>().OrderBy(x => x.Id),
            createSeededFactory());

        var processed = new List<string>();
        await stream
            .Map(x => $"{x.Id}:{x.Name}")
            .Filter(x => x.Contains(':'))
            .Take(2)
            .ForEachAsync(processed.Add);

        Assert.That(processed, Is.EqualTo(new[] { "1:A", "2:B" }));
    }

    [Test]
    public async Task ToStream_Extension_UsesFactoryPathAndEmitsData()
    {
        Func<DbContext> factory = createSeededFactory();
        var stream = factory.ToStream(ctx => ctx.Set<TestEntity>().Where(x => x.IsActive).OrderBy(x => x.Id));

        var ids = await stream.Map(x => x.Id).ToListAsync();

        Assert.That(ids, Is.EqualTo(new[] { 1, 3 }));
    }

    static Func<DbContext> createSeededFactory(ConcurrentBag<int>? disposedContextIds = null)
    {
        var instanceCounter = 0;
        var databasePrefix = $"ef-stream-tests-{Guid.NewGuid():N}";

        return () =>
        {
            var instanceId = Interlocked.Increment(ref instanceCounter);
            var options = new DbContextOptionsBuilder<TrackingDbContext>()
                .UseInMemoryDatabase($"{databasePrefix}-{instanceId}")
                .Options;

            var context = new TrackingDbContext(
                options,
                instanceId,
                id => disposedContextIds?.Add(id));

            context.Database.EnsureCreated();
            context.Entities.AddRange(
                new TestEntity { Id = 1, IsActive = true, Name = "A" },
                new TestEntity { Id = 2, IsActive = false, Name = "B" },
                new TestEntity { Id = 3, IsActive = true, Name = "C" });
            context.SaveChanges();

            return context;
        };
    }
}

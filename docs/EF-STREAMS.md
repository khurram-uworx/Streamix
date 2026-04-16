# Entity Framework Streams Design

## Overview

This document outlines the design and implementation plan for Entity Framework Core integration with Streamix. The goal is to enable enterprise use cases where users can model business problems using reactive streams over database data.

## Design Principles

1. **Minimal Requirements**: Place minimal constraints on entity models - only require what EF Core itself requires
2. **Provider Neutral**: Work with any EF Core database provider (SQL Server, PostgreSQL, SQLite, etc.)
3. **Reactive First**: Treat database queries as reactive data sources that can be composed with other streams
4. **Resource Safety**: Ensure proper disposal of DbContext instances
5. **Composition**: Enable EF streams to participate fully in the reactive stream ecosystem

## Core Concepts

### Entity Framework Stream

An `IStream<T>` that:
- Executes an EF Core query when subscribed to
- Emits entities as they are materialized from the database
- Properly disposes the DbContext after query completion
- Supports cancellation and error handling
- Can be composed with other stream operators

### Key Features

1. **Lazy Execution**: Query executes only when stream is subscribed to
2. **Cancellation Support**: Database query can be cancelled via stream cancellation
3. **Resource Management**: Automatic DbContext disposal
4. **Composition**: Full participation in stream operator ecosystem
5. **Error Handling**: Proper propagation of database and query errors

## API Design

### Core Extension Methods

```csharp
// Basic EF stream creation
IStream<T> FromEntityFramework<T>(this IQueryable<T> queryable, Func<DbContext> dbContextFactory, string? name = null)
    where T : class;

// With custom clock for testing
IStream<T> FromEntityFramework<T>(this IQueryable<T> queryable, Func<DbContext> dbContextFactory, IClock clock, string? name = null)
    where T : class;
```

### Usage Examples

```csharp
// Basic usage with DbContext factory
var customersStream = dbContext.Customers.Where(c => c.IsActive)
    .FromEntityFramework(() => new MyDbContext());

// Composition with other operators
var processedStream = customersStream
    .Map(customer => ProcessCustomer(customer))
    .Filter(customer => customer.OrderCount > 0)
    .Take(100);

// With cancellation
var cts = new CancellationTokenSource();
await processedStream.ForEachAsync(customer => 
{
    Console.WriteLine(customer.Name);
}, cts.Token);

// Error handling
var safeStream = customersStream
    .OnErrorResume(ex => Stream.Just(new Customer { Name = "Fallback" }))
    .Log("CustomerStream");
```

## Implementation Components

### 1. EntityFrameworkStream<T> Implementation

- Internal class implementing `IStream<T>`
- Wraps `IQueryable<T>` and `Func<DbContext>`
- Handles query execution, cancellation, and resource disposal
- Implements all required stream operations

### 2. Extension Methods

- `FromEntityFramework` overloads in `StreamExtensions`
- Provide fluent API for creating EF streams
- Handle parameter validation and stream creation

### 3. Integration Points

- Full participation in stream operator ecosystem
- Support for all standard stream operations
- Proper clock and naming support

## Error Handling

### Expected Errors

- `OperationCanceledException`: When query is cancelled
- `DbUpdateException`: When database constraints are violated
- `InvalidOperationException`: When EF Core encounters issues
- `SqlException` (or provider-specific): Database connectivity issues

### Error Propagation

- Errors during query execution propagate through the stream
- Errors are handled by standard stream error handling operators
- DbContext is properly disposed even when errors occur

## Performance Considerations

### Query Execution

- Query materialization happens during enumeration
- `ToListAsync()` is used to ensure proper resource disposal
- Query is executed once per subscription (cold observable pattern)

### Memory Usage

- Entities are yielded one at a time from the materialized list
- No additional buffering beyond what EF Core provides
- DbContext is disposed after query completion

### Concurrency

- Each subscription creates its own DbContext
- Multiple subscribers can execute queries concurrently
- Standard stream concurrency operators work as expected

## Testing Strategy

### Test Coverage Areas

1. **Basic Functionality**: Query execution and entity emission
2. **Cancellation**: Query cancellation via stream cancellation
3. **Error Handling**: Various database and query errors
4. **Resource Safety**: DbContext disposal in all scenarios
5. **Composition**: Integration with other stream operators
6. **Performance**: Memory usage and query execution patterns

### Test Scenarios

- Successful query execution with various entity types
- Query cancellation at different stages
- Error handling and propagation
- Multiple concurrent subscriptions
- Composition with Map, Filter, Take, etc.
- Integration with ConnectableStream

## Enterprise Use Cases

### Real-world Scenarios

1. **Real-time Dashboards**: Stream database data to UI components
2. **Data Processing Pipelines**: Process database records reactively
3. **Event-driven Architectures**: React to database changes via streams
4. **Microservices Integration**: Stream data between services
5. **Reporting Systems**: Generate reports from streaming database data

### Example: Order Processing

```csharp
// Stream new orders from database
var newOrders = dbContext.Orders
    .Where(o => o.Status == OrderStatus.New && o.CreatedDate > DateTime.UtcNow.AddHours(-1))
    .FromEntityFramework(() => new OrderDbContext())
    .Named("NewOrders");

// Process orders concurrently
var processedOrders = newOrders
    .FlatMap(order => ProcessOrderAsync(order), maxConcurrency: 10)
    .Log("OrderProcessor");

// Update database with results
await processedOrders.ForEachAsync(processedOrder =>
{
    using var updateContext = new OrderDbContext();
    updateContext.Orders.Update(processedOrder);
    await updateContext.SaveChangesAsync();
});
```

## Roadmap

### Phase 1: Core Implementation (Current)

- Basic EF stream implementation
- Extension methods for easy creation
- Integration with existing stream operators
- Comprehensive test coverage

### Phase 2: Advanced Features

- Change tracking integration
- Transaction support
- Batch operations
- Performance optimizations

### Phase 3: Integration Patterns

- CQRS patterns with streams
- Event sourcing integration
- Cache invalidation patterns
- Real-time sync patterns

## Non-Goals

1. **ORM Replacement**: Not replacing EF Core's ORM capabilities
2. **Query Builder**: Not creating a new query DSL
3. **Database Abstraction**: Not abstracting away EF Core
4. **Migration Tool**: Not handling database schema migrations

## Benefits

1. **Enterprise Readiness**: Enables real enterprise use cases
2. **Reactive Patterns**: Brings reactive programming to database access
3. **Composition**: Seamless integration with existing stream ecosystem
4. **Testability**: Easy to test with in-memory databases
5. **Flexibility**: Works with any EF Core provider
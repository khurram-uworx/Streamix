# Add Timestamped<T> and WindowByTime (tumbling + sliding) for time-series stream processing

## **Summary**

Introduce first-class support for time-series processing in Streamix by:

* Adding a `Timestamped<T>` data wrapper
* Implementing time-based windowing via `WindowByTime(...)`
* Supporting both tumbling and sliding windows
* Keeping the core `Stream<T>` abstraction unchanged

This enables scenarios like:

> “Compute 30-minute rolling max temperature from a live stream”

without introducing new stream types or breaking the current mental model.

---

## **Motivation**

Current Streamix operators support:

* count-based batching (`Buffer`, `Window`)
* concurrency and backpressure
* async composition

However, **time-based segmentation is missing**, which is essential for:

* metrics aggregation
* telemetry pipelines
* IoT/event streams
* financial time-series

We explicitly want to support **event-time processing**, not implicit system time.

---

## **Design Principles**

* ✅ Keep `IStream<T>` as the only stream abstraction
* ✅ Model time as **data**, not stream metadata
* ✅ Require explicit timestamps for deterministic behavior
* ✅ Avoid Reactor/Rx-style operator explosion
* ✅ Keep API minimal and composable

---

## **Proposed API**

### 1. `Timestamped<T>`

```csharp
public readonly record struct Timestamped<T>(
    T Value,
    DateTimeOffset Timestamp);
```

Optional helpers (nice-to-have, not required initially):

```csharp
public static Timestamped<T> Create(T value, DateTimeOffset timestamp);
```

---

### 2. `WindowByTime`

```csharp
IStream<IStream<Timestamped<T>>> WindowByTime(
    TimeSpan duration,
    TimeSpan? slide = null,
    int capacity = 16,
    ChannelBackpressureMode mode = ChannelBackpressureMode.Wait);
```

---

## **Behavioral Semantics**

### General

* Input must be `IStream<Timestamped<T>>`
* Windows are emitted as **cold, single-consumer streams**
* Windows are based on **event time (`Timestamp`)**, not system time
* Items are assigned to windows based on timestamp inclusion rules

---

### Tumbling Windows (`slide == null`)

* Windows are non-overlapping
* Each window spans `[start, start + duration)`
* Items belong to exactly one window
* Last window emits remaining items on completion

---

### Sliding Windows (`slide != null`)

* Windows are emitted every `slide`
* Each window spans `[start, start + duration)`
* Items may belong to multiple windows
* Overlapping windows are expected

---

### Inclusion Rules (important)

* Lower bound: **inclusive**
* Upper bound: **exclusive**

```text
[start, end)
```

---

### Ordering Assumptions

* Input is assumed to be **monotonic by timestamp** (at least non-decreasing)
* Out-of-order handling is **out of scope (v1)**

---

### Backpressure

* Windows internally use bounded channels
* `capacity` and `mode` control buffering behavior
* Backpressure propagates to upstream

---

### Completion Semantics

* When upstream completes:

  * All active windows are completed
  * Remaining buffered items are emitted

---

### Cancellation

* Cancelling downstream:

  * Stops window emission
  * Cancels all active window streams

---

## **Example Usage**

### Tumbling window (30 min max)

```csharp
await temperatureStream
    .WindowByTime(TimeSpan.FromMinutes(30))
    .FlatMap(window =>
        window.MaxAsync(x => x.Value))
    .ForEachAsync(Console.WriteLine);
```

---

### Sliding window (30 min window, 1 min slide)

```csharp
await temperatureStream
    .WindowByTime(
        duration: TimeSpan.FromMinutes(30),
        slide: TimeSpan.FromMinutes(1))
    .FlatMap(window =>
        window.MaxAsync(x => x.Value))
    .ForEachAsync(Console.WriteLine);
```

---

## **Non-Goals (Important)**

* ❌ No `TemporalStream<T>`
* ❌ No implicit clock (`DateTimeOffset.UtcNow`)
* ❌ No out-of-order / watermark handling (future work)
* ❌ No additional time operators in this issue (`BufferByTime`, `Sample`, etc.)

---

# ⚠️ Risks & Considerations

* Higher-order streams (`IStream<IStream<T>>`) increase complexity
* Sliding windows can be memory-heavy if misused
* Requires strong test coverage for boundary correctness

---

# 🚀 Future Extensions (NOT part of this issue)

* `BufferByTime`
* Watermarks / late event handling
* Session windows
* Time-based joins

# Tasks

## 1. Core Type
* [✅] Implement `Timestamped<T>`
* [✅] Add XML docs
* [✅] Add basic unit tests

## 2. WindowByTime – Tumbling
* [✅] Implement `WindowByTime(duration)` (slide = null)
* [✅] Channel-based window segmentation
* [✅] Ensure correct boundary handling `[start, end)`
* [✅] Add tests:

  * exact boundary alignment
  * partial final window
  * empty stream
  * single item

## 3. WindowByTime – Sliding
* [ ] Extend implementation to support `slide`
* [ ] Ensure overlapping window correctness
* [ ] Avoid unbounded memory growth
* [ ] Add tests:

  * overlapping membership
  * dense sliding (slide < duration)
  * sparse sliding (slide > duration)

## 4. Backpressure + Capacity
* [ ] Respect `capacity` and `ChannelBackpressureMode`
* [ ] Add stress tests for slow consumers

## 5. Completion + Cancellation
* [ ] Ensure all active windows complete on upstream completion
* [ ] Ensure cancellation propagates correctly
* [ ] Add tests

## 6. Documentation
* [ ] Add section in README:

  * “Time-based operators”
* [ ] Provide 2–3 real-world examples
* [ ] Clarify event-time requirement

## 7. (Optional, Nice-to-Have)
* [ ] Add helper:

```csharp
MapWithTimestamp(Func<T, DateTimeOffset>)
```

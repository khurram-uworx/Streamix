## ✅ First Release Scope

The first usable release is intentionally focused on the core pull-based Streamix model:

* `Stream<T>` and `Single<T>` over `IAsyncEnumerable<T>`
* Core transformation, flattening, batching, resilience, and time-based operators documented above
* Explicit concurrency, ordering, cancellation, and backpressure behavior
* Hot-stream primitives: `Publish`, `Replay`, and `RefCount`
* Terminal/materialization operators and sink/channel interop
* Optional AsyncRx.NET interop in the separate `Streamix.Extensions` package

Intentionally deferred from the first release:

* Structured concurrency support
* ASP.NET Core integration for reactive endpoints
* Additional time-based operators beyond the currently documented set
* Source generators for optimized pipelines

If something is not listed above and not documented as shipped in this README, treat it as outside the first-release MVP rather than an accidentally missing feature.

---

## 📋 Release Checklist

Before publishing a release:

* Verify the root `README.md` and package readmes describe only shipped behavior.
* Run `dotnet restore`, `dotnet build --configuration Release`, and `dotnet test --configuration Release` from the repo root.
* Confirm package versions and NuGet metadata are intentional for `Streamix` and `Streamix.Extensions`.
* Keep roadmap items and MVP scope clearly separated so deferred work is not presented as available behavior.

---

## Status

- Streamix is still in an early stage. The root repository README is the authoritative product contract and may describe work that is still being completed.
- Streamix is still early-stage, but this package README is intended to describe the shipped core surface only.
Use the root repository README for the fuller product contract, roadmap, and release checklist.

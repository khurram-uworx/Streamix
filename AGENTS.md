# AGENTS.md

## Project overview
- Streamix is a .NET library project for idiomatic reactive streams in C#.
- The product definition currently lives in `README.md`; treat it as the primary specification unless the user says otherwise.
- The repo is in an early stage. Expect incomplete implementation, missing operators, and evolving API decisions.
- Use `docs/TASKS-TEMPLATE.md` when you need to create a new assignable task breakdown for a feature area or review follow-up.

## Repository map
- `src/Streamix`: main production library targeting `net10.0`.
- `src/Streamix.Extensions`: extension operators and helpers layered on the core library.
- `src/Streamix.AspNetCore`: ASP.NET Core integration package.
- `tests/Streamix.Benchmarks`: benchmark project for performance exploration.
- `tests/Streamix.Tests`: NUnit test project covering library behavior.
- `README.md`: product contract, examples, design principles, and roadmap.
- `Streamix.slnx`: solution entry point.
- `.github/workflows/ci.yml`: CI workflow for restore/build/test and coverage upload.

## Dev environment tips
- Required SDK: .NET 10.
- Restore/build/test from repo root:
  - `dotnet restore`
  - `dotnet build --configuration Release`
  - `dotnet test --configuration Release`
- During iteration, targeted checks are fine when they meaningfully reduce cycle time:
  - `dotnet test tests/Streamix.Tests/Streamix.Tests.csproj`
  - `dotnet test --filter <NameOrCategory>`
- CI mirrors the root workflow with explicit step chaining:
  - `dotnet restore`
  - `dotnet build --no-restore --configuration Release`
  - `dotnet test --no-build --configuration Release --verbosity normal --collect:"XPlat Code Coverage"`
- There are currently no known external service dependencies for the test suite.

## Agent workflow
- Read `README.md` before implementing features that affect public API or behavior.
- Verify the current repo state before editing; this project is intentionally in flux.
- Keep changes tightly scoped to the requested task or the selected backlog item.
- Prefer implementing real behavior with tests rather than scaffolding placeholder APIs.
- For new public Streamix operators or factory helpers, update `README.md`, `GETTING-STARTED.md`, `ARCHITECTURE.md`, tests, and examples where relevant.
- Treat `examples/AIDataEngg` as an ergonomics regression target for complex workflow composition; keep it current when changes affect async factories, retry/recovery, checkpoints, or terminal side-effect patterns.
- If the README and code conflict, call that out in your final response and either:
  - align the code to the README, or
  - update the README if the user asked to change the contract.

## Testing Conventions

- **Framework:** NUnit 4.x — `[Test]`, `Assert.That(...)`, `Assert.ThrowsAsync`, no `[TestCase]`
- **Naming:** `Method_Scenario_ExpectedBehavior` PascalCase
- **Pattern:** Arrange-Act-Assert (AAA); no explicit comments needed
- **Organization:** one test class per source class, `*Tests.cs` suffix; split by behavior when a class is large
- **Mocking:** prefer real implementations where feasible; use `Substitute.For<T>()` only when external dependencies require it
- For behavior changes, add or update tests in `tests/Streamix.Tests`.
- Treat README examples as candidate executable tests where practical.
- For stream operators, tests should cover: success behavior, cancellation, exception propagation, ordering semantics where relevant.

## Code Style

- `.editorconfig` at repo root is authoritative; follow it over any convention below.
- **Private fields:** `camelCase` without `_` prefix (`logger`, not `_logger`).
- **Member ordering:** inner classes → constructors → properties → methods; static before instance; private → protected → internal → public.
- **Primary constructors:** preferred for service/DI classes over classic constructor with field assignment.
- **Sealed by default:** use `sealed class` for non-abstract classes unless inheritance is explicitly designed.
- **Collection expressions:** `[]` for empty/static collections; `new List<T>()` or `new Dictionary<K,V>()` for mutable ones.
- **Nullable reference types:** enabled; do not introduce avoidable warnings.
- **Omit braces** from single-line `if`/`else` bodies when the body fits one line and is on the same line as the condition.
- **No comments** in generated code unless explaining a non-obvious design decision.
- **No Hungarian notation** — no prefixes encoding scope or mutability.
- Prefer record types for simple data carriers (e.g. config models, DTOs) and classes for entities/services.
- Prefer small, clear public APIs. This repo is early enough that surface area discipline matters more than convenience overloads.
- Prefer .NET-idiomatic naming and semantics over mechanically copying Reactor or Rx naming.
- Use `IAsyncEnumerable<T>` as the default mental model unless concurrency or hot-stream behavior specifically requires channels or other machinery.
- Be explicit about cancellation, error propagation, buffering, and ordering semantics in both code and tests.
- It is fine to keep closely related small types in the same file when that improves locality.

## Documentation expectations
- If a change alters the intended public contract, update `README.md`.
- If a change alters operator ergonomics or semantics, update `GETTING-STARTED.md` and `ARCHITECTURE.md` as well.
- If a change affects task sequencing or reveals a constraint that materially impacts downstream work, document it in `WORK.md` (Create it if required).
- Do not treat aspirational roadmap items as already implemented.
- Keep examples truthful; avoid documenting APIs that do not exist yet.

## PR instructions
- Keep changes focused; avoid unrelated refactors.
- Before opening a PR, run:
  - `dotnet restore`
  - `dotnet build --configuration Release`
  - `dotnet test --configuration Release`
- Summaries should mention:
  - what behavior changed
  - what tests were added or updated
- If implementation had to diverge from the README, document that clearly in the PR notes.

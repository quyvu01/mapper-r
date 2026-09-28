# MapperR

A free, open-source object-to-object mapper for .NET, in the spirit of AutoMapper — with an optional
code generator (`maprgen`) that turns your mapping profiles into plain C# for zero-reflection, AOT-friendly
mapping.

> **Status: early development.** Not yet published to NuGet. APIs may change.

## Why

- **Familiar configuration** — `Profile` + `CreateMap<TSource, TDestination>()`, just like AutoMapper.
- **One source of truth** — every type pair compiles to a single expression tree. The runtime engine
  compiles it; the generator prints the *same* tree as C#, so both paths always behave identically.
- **Fail fast** — missing profiles, self-referencing maps and circular profile dependencies throw a clear
  error when a mapper is first resolved, not deep inside a mapping.
- **Opt-in code generation** — generated mappers replace runtime ones per type pair; anything that
  cannot be generated keeps working through the runtime engine.
- **Apache 2.0** — no commercial license tiers.

## Quick start

Define a profile:

```csharp
using MapperR.Core.Abstractions;

public class MappingProfiles : Profile
{
    public override void AddProfiles()
    {
        CreateMap<Address, AddressResponse>();                  // convention only

        CreateMap<Person, PersonResponse>()
            .ForMember(d => d.Name, s => $"{s.Name} (VIP)")     // computed value
            .ForMember(d => d.Address, s => s.Address);         // nested map via the profile above
    }
}
```

Register and use it:

```csharp
using MapperR.Core.Abstractions;
using MapperR.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddMapR(cfg => cfg.AddProfilesFromAssembly(typeof(MappingProfiles).Assembly));

var mapper = services.BuildServiceProvider().GetRequiredService<IMapper>();

var response = mapper.Map<Person, PersonResponse>(person);  // source type known at compile time
var other    = mapper.Map<PersonResponse>(somePerson);       // source type resolved at runtime
```

## What gets mapped

| Case | How |
|---|---|
| Same name, same or assignable type | Automatic (case-insensitive name match) |
| Numbers and enums (`int` → `long`, `int?` → `int`, `EnumA` → `EnumB`) | Automatic, when convertible |
| Computed / renamed values | `.ForMember(d => d.X, s => <any expression>)` |
| Nested objects | `.ForMember(d => d.Child, s => s.Child)` + a `CreateMap` for the child pair; inlined, null-safe |
| Collections (`List<T>`, `T[]`, `IEnumerable<T>`, `HashSet<T>`, ...) | `.ForMember(d => d.Items, s => s.Items)` + a `CreateMap` for the element pair (or convertible elements); `null` collection → `null` |

Current limitations: destination types need a public parameterless constructor; self-referencing or
circular mappings are rejected; `null` elements *inside* a mapped collection are not guarded; the
destination-side selector of `ForMember` must be a direct member (`d => d.Name`, not `d => d.A.Name`).

## Code generation with `maprgen`

`maprgen` loads your **built** assembly, discovers profiles exactly like `AddMapR` does, and writes one
generated class per type pair plus an `AddGeneratedMappers()` registration method.

```bash
dotnet build MyApp.csproj
maprgen generate --assembly bin/Debug/net10.0/MyApp.dll --output Generated
dotnet build MyApp.csproj    # compiles Generated/MapperR.Generated.g.cs
```

```csharp
using MyApp; // generated namespace defaults to the assembly name (override with --namespace)

services.AddMapR(cfg => cfg.AddProfilesFromAssembly(typeof(MappingProfiles).Assembly))
        .AddGeneratedMappers();
```

- Calling `AddGeneratedMappers()` is optional. Microsoft DI prefers the generated closed-generic
  registration over the runtime open-generic one, and pairs that were not generated fall back automatically.
- Pairs that cannot be reproduced as source (captured variables in a `ForMember` lambda, private setters,
  inaccessible types) are skipped with `warning MAPRGEN001` and stay on the runtime engine.
- **Keep it in sync:** `maprgen verify` (same arguments) exits with code `1` when the generated file no
  longer matches the profiles — run it in CI.
- **First run in a new project:** code calling `AddGeneratedMappers()` cannot build before the file exists.
  Generate once before adding the call, or add a stub `AddGeneratedMappers(this IServiceCollection s) => s;`
  at the output path and let `maprgen` overwrite it.
- `maprgen` must match the `MapperR.Core` version your app references; it reports an error otherwise.

Install it from source as a global tool:

```bash
dotnet pack src/MapperR.Generator -c Release -o ./artifacts
dotnet tool install --global MapperR.Generator --add-source ./artifacts
```

## Performance

Measured with BenchmarkDotNet (.NET 10, Apple Silicon); see [`tests/Benchmark`](tests/Benchmark).

| Per call | Hand-written | AutoMapper 14 | MapperR runtime | MapperR generated |
|---|---|---|---|---|
| Flat object (7 members) | 7.1 ns | 45.5 ns | 21.8 ns | 20.6 ns |
| Nested object + 10-item list | 127 ns | 195 ns | 367 ns | 137 ns |

| Configuration + first map | Time | Allocated |
|---|---|---|
| AutoMapper 14 | 1,191 µs | 214.6 KB |
| MapperR runtime | 370 µs | 104.3 KB |
| MapperR generated | 2.3 µs | 9.6 KB |

The runtime engine is currently slower than AutoMapper for collections; the suspected cause (a nested
lambda re-created per call inside the compiled `Select`) is being investigated. Generated mappers are
within ~10% of hand-written code.

## Repository layout

| Path | Contents |
|---|---|
| `src/MapperR.Core` | The mapper: profiles, expression builder, runtime engine (net8.0 / net9.0 / net10.0) |
| `src/MapperR.Generator` | `maprgen` CLI: expression → C# printer and code generator |
| `tests/MapperR.Core.Tests` | Unit tests for the core (xUnit + Shouldly) |
| `tests/MapperR.Generator.Tests` | Printer, generator and runtime-vs-generated equivalence tests |
| `tests/FrameworkTest` | Small console sample using runtime + generated mappers |
| `tests/Benchmark` | BenchmarkDotNet comparison against AutoMapper 14 |

## Build and test

```bash
dotnet build MapperR.slnx
dotnet test tests/MapperR.Core.Tests
dotnet test tests/MapperR.Generator.Tests
dotnet run -c Release --project tests/Benchmark -- --filter '*'
```

## Roadmap

`ProjectTo<T>()` for `IQueryable`/EF Core, `Condition`, `Ignore`, `ReverseMap`, constructor/record
mapping, MSBuild integration for `maprgen`, and NuGet packages.

## License

[Apache License 2.0](LICENSE)

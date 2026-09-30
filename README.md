<p align="center">
  <img src="assets/icon.svg" width="96" height="96" alt="MapperR icon">
</p>

# MapperR

A free, open-source object-to-object mapper for .NET, in the spirit of AutoMapper — with an optional
code generator (`maprgen`) that turns your mapping profiles into plain C# for zero-reflection, AOT-friendly
mapping.

> **Status: early development.** Not yet published to NuGet. APIs may change.

## Why

- **Familiar configuration** — `Profile` + `CreateMap<TSource, TDestination>()`, just like AutoMapper.
- **One source of truth** — every type pair compiles to a single expression tree. The runtime engine
  compiles it; the generator prints the *same* tree as C#, so both paths always behave identically.
- **Real object graphs** — self-references, types that reference each other, and cycles in the data are
  supported: an object reached from several places is mapped once, so the result has the same shape.
- **Fail fast** — missing profiles, a `ForMember` that cannot be mapped, or a `CreateMap` between two
  collection types throw a clear error, not something obscure deep inside a mapping.
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

        CreateMap<Person, PersonResponse>()                     // Address is mapped too: its pair is registered
            .ForMember(d => d.Name, s => $"{s.Name} (VIP)")     // computed value
            .Ignore(d => d.Password);                           // never copied
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

var response = mapper.Map<Person, PersonResponse>(person);   // source type known at compile time
var other    = mapper.Map<PersonResponse>(somePerson);        // source type resolved at runtime
var all      = mapper.Map<PersonResponse[]>(people);          // any collection of Person; see "Collections"
mapper.Map(person, existingResponse);                         // update an existing object in place
```

Options, set in the same `AddMapR` call:

```csharp
services.AddMapR(cfg =>
{
    cfg.AddProfilesFromAssembly(typeof(MappingProfiles).Assembly);
    cfg.AllowNullCollections = true;   // default false: a null collection maps to an empty one
});
```

## What gets mapped

| Case | How |
|---|---|
| Same name, same or assignable type | Automatic (case-insensitive name match) |
| Numbers and enums (`int` → `long`, `int?` → `int`, `EnumA` → `EnumB`) | Automatic, when convertible. A null `int?` becomes `0`. An enum maps to a *different* enum by numeric value (not by name) for now |
| Scalar → `string` (numbers, `bool`, `char`, enums, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`) | Automatic, always with the invariant culture (`1.5`, never `1,5`). A null `int?` becomes `null` |
| Computed / renamed values | `.ForMember(d => d.X, s => <any expression>)` |
| Members to leave alone | `.Ignore(d => d.X)` — not copied, and kept as they are when updating an existing object |
| Nested objects | Automatic when a `CreateMap` exists for the child pair; null-safe. Pairs may reference each other |
| Collections (`List<T>`, `T[]`, `IEnumerable<T>`, `HashSet<T>`, ...) | Automatic when the element pair is registered or the elements are convertible; see "Collections" |
| `mapper.Map(source, destination)` | Same values as `Map(source)`, written onto `destination`: nested objects are updated in place, collections are replaced, members without a mapping keep their value |

Current limitations: destination types need a public parameterless constructor; the destination-side
selector of `ForMember` must be a direct member (`d => d.Name`, not `d => d.A.Name`); collections have the
limits listed below.

## Collections

A collection is mapped through its element pair — register `CreateMap<Src, Dst>()` and every collection of
those comes for free, as a member or on its own:

```csharp
Dst[]       array = mapper.Map<Dst[]>(listOfSrc);
List<Dst>   list  = mapper.Map<List<Dst>>(srcArray);
HashSet<Dst> set  = mapper.Map<HashSet<Dst>>(srcSequence);   // arrays, lists, sets, LINQ queries, ...
long[]      ids   = mapper.Map<long[]>(new List<int> { 1, 2 });
```

- **Destinations:** a one-dimensional array, `List<T>`, `HashSet<T>`, and `IEnumerable<T>`,
  `ICollection<T>`, `IList<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>` (these come back as a
  `List<T>`). Anything else is reported with a clear message.
- **`null`:** a null source collection maps to an empty one — an empty array, list or set, never `null` —
  unless you set `AllowNullCollections = true`. `null` elements inside a collection stay `null`.
- **Elements** can be objects with their own profile, or scalars (`int` → `long`, `int` → `string`, ...).
  Elements of one collection share a context, so an object reached from several of them is still one object.
- **No profile for the collection itself.** `CreateMap<List<Src>, List<Dst>>()` (and `Src` → `List<Dst>`,
  `List<Src>` → `Dst`) throws as soon as the mapper reads its profiles (its first use), pointing at the element
  pair to register instead.
  A type of your own that derives from a collection (say a paged list with a total count) is still an
  ordinary pair.
- **Not supported yet:** collections of collections (`Dst[][]`), `Dictionary<,>`, other destination types
  (`Queue<T>`, `ReadOnlyCollection<T>`, ...), and mapping *onto* an existing collection
  (`Map(list, existingList)` throws `NotSupportedException`).

## Object graphs with cycles

A pair that can reach itself — `Employee.Manager`, or `Order` ↔ `Line` with a back reference — needs no
special configuration. Nested members are mapped by calling the child pair's mapper at runtime, so building a
mapper never recurses, and pairs that can be part of a cycle remember the objects they already mapped:

```csharp
var loop = new Node(); loop.Next = loop;
var dto = mapper.Map<NodeDto>(loop);   // dto.Next == dto
```

The runtime engine throws an `InvalidOperationException` beyond 256 nested levels rather than overflowing the
stack. Generated mappers rely on the same reference tracking and have no depth counter yet.

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
- Generated mappers call each other directly, map collections without LINQ, and read `AllowNullCollections`
  at runtime — one generated file serves either setting.
- `mapper.Map<Dst[]>(list)` (a collection with no profile of its own) runs on the runtime engine; its
  elements still go through the generated mappers.
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

Measured with BenchmarkDotNet (.NET 10, Apple Silicon, three launches per benchmark); see
[`tests/Benchmark`](tests/Benchmark). Steady-state cost of one `Map` call:

| Per call | Hand-written | AutoMapper 14 | MapperR runtime | MapperR generated |
|---|---|---|---|---|
| Flat object (7 members) | 7.6 ns | 47 ns | 26 ns | 24 ns |
| Nested object + 10-item list | 132 ns | 211 ns | 151 ns | 131 ns |

A larger graph — a company with departments and employees that point back at each other (cycles, shared
objects, nested value types, several collection shapes), same mapping work in every column:

| Per call | AutoMapper 14 | MapperR runtime | MapperR generated |
|---|---|---|---|
| Order + 10 lines (no cycles) | 203 ns / 976 B | 136 ns / 784 B | 116 ns / 784 B |
| Organisation, 4 employees | 2.17 µs / 3.1 KB | 1.21 µs / 2.9 KB | 0.88 µs / 2.8 KB |
| Organisation, 211 employees | 105 µs / 155 KB | 61 µs / 147 KB | 43 µs / 139 KB |

| Configuration + first map | Time | Allocated |
|---|---|---|
| AutoMapper 14 | 1,203 µs | 214 KB |
| MapperR runtime | 776 µs | 407 KB |
| MapperR generated | 4.0 µs | 14.8 KB |

AutoMapper's rows on the no-cycle graphs jump between runs (its mean is about a third above its median), so
the tables show medians there. The runtime engine's first call costs more than it did in earlier versions
(370 µs and 104 KB), while its steady-state numbers improved; generated mappers have almost no setup cost.

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
dotnet run -c Release --project tests/Benchmark -- --filter '*' --launchCount 3
```

## Roadmap

`ProjectTo<T>()` for `IQueryable`/EF Core, `Condition`, `ReverseMap`, constructor/record mapping, mapping
enums by name, more collection support (`Dictionary<,>`, collections of collections, more destination types,
mapping onto an existing collection), MSBuild integration for `maprgen`, and NuGet packages.

## License

[Apache License 2.0](LICENSE)

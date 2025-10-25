# Bitbound.Analyzers

A collection of Roslyn analyzers used in Bitbound's projects.

## Current status

As of this writing, `MemberOrder` is the only analyzer implemented in this repository. It enforces a consistent ordering of members inside types. Additional analyzers may be added in future updates.

## Project layout

- `Bitbound.Analyzers.MemberOrder/` — Analyzer implementation and diagnostic definitions.
- `Bitbound.Analyzers.MemberOrder.CodeFixes/` — Code fix provider(s) for the `MemberOrder` analyzer.
- `Bitbound.Analyzers.MemberOrder.Package/` — Packaging project for producing distributable artifacts.
- `Bitbound.Analyzers.MemberOrder.Test/` — Unit tests for the analyzer and code fixes.

## Building

The repository uses the .NET SDK. From the repository root you can build the solution with:

```pwsh
dotnet build Bitbound.Analyzers.slnx
```

To build a single project (for example the analyzer project):

```pwsh
dotnet build Bitbound.Analyzers.MemberOrder\Bitbound.Analyzers.MemberOrder.csproj
```

## Running tests

Run the unit tests from the solution root:

```pwsh
dotnet test
```

Or run the specific test project:

```pwsh
dotnet test Bitbound.Analyzers.MemberOrder.Test\Bitbound.Analyzers.MemberOrder.Test.csproj
```

## Contributing

Contributions are welcome. If you'd like to add a new analyzer or improve the existing `MemberOrder` implementation:

1. Open a feature branch.
2. Add/update analyzer project(s) under a new folder alongside existing analyzers.
3. Add unit tests in the corresponding `.Test` project.
4. Ensure `dotnet build` and `dotnet test` pass locally.
5. Open a pull request describing your changes.

## License

This project is licensed under the terms in `LICENSE.txt`, which is an MIT license.

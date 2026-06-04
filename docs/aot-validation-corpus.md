# AOT Validation Corpus

This note records the **real-world corpus** used to validate the AOT XAML/`.csproj` analyzers
(`AotCompatibilityAnalyzer.CheckXaml` / `CheckBuildConfig`) and the one **deliberately-accepted
finding** that surfaces on the real apps.

## Why

The AOT pitfalls catalogue that drives `AotCompatibilityAnalyzer` originates from a family of
shipped **Avalonia 12 + .NET 10 Native AOT** desktop apps. Those same apps are the ideal corpus to
check the analyzer against: they are configured *correctly* for AOT, so the analyzer should produce
**few or no false positives** on them.

> The analyzers are unit-tested without an MSBuild workspace via the `internal` string seams
> `AotCompatibilityAnalyzer.AnalyzeXaml(...)` / `AnalyzeBuildConfig(...)` (Core exposes
> `InternalsVisibleTo RoslynMcpServer.Tests`). See `RoslynMcpServer.Tests/Unit/Services/AotValidationCorpusTests.cs`
> and the fixtures under `RoslynMcpServer.Tests/Fixtures/Aot/`.

## Corpus

All six are Avalonia 12 / .NET 10 / Native AOT / `[LibraryImport]` apps (Cheat-Engine-adjacent
game tooling). The relevant axis for `CheckBuildConfig` is whether they reference `Avalonia.Desktop`
on `win-x64`:

| Repo (local) | `BuiltInComInteropSupport=false` | `AvaloniaUseCompiledBindingsByDefault=true` | `TrimmerRootAssembly` | References `Avalonia.Desktop` | Expected `CheckBuildConfig` |
|---|:--:|:--:|:--:|:--:|---|
| `D:\Github\ZoltDump` | ✅ | ✅ | ✅ | ❌ (uses Win32/Skia/HarfBuzz) | **0 issues** |
| `D:\Github\AOBMaker` | ✅ | ✅ | ✅ | ❌ | **0 issues** |
| `D:\Github\discrete` | ✅ | ✅ | ✅ | ❌ | **0 issues** |
| `D:\Github\UE5CEDumper` | ✅ | ✅ | ✅ | ✅ | **1 issue** (accepted, below) |
| `D:\Github\AchievoLab` | varies | ✅ | varies | ✅ (AnSAM, RunGame, MyOwnGames) | Desktop finding (+ any genuinely-missing settings) |
| `D:\Github\CrimsonAtomtic` | ✅ | ✅ | — | ✅ | Desktop finding |

> Note: an earlier raw text grep "found" `Avalonia.Desktop` in ZoltDump/AOBMaker too — but only inside
> a **comment** explaining why they avoid it. The XML-based `CheckBuildConfig` matches the
> `<PackageReference Include="Avalonia.Desktop">` *element*, so it correctly ignores the comment.
> This is exactly the regex→XML robustness win from the AOT analyzer refactor.

## The accepted finding: `Avalonia.Desktop` on `win-x64`

`CheckBuildConfig` flags (Medium) when a project references `Avalonia.Desktop` while targeting
`win-x64`, because `Avalonia.Desktop` drags `Avalonia.X11` + `Avalonia.FreeDesktop` +
`Tmds.DBus.Protocol` into the graph, and ILC then emits "will always throw" diagnostics for the
trimmed Linux entrypoints.

**This is a true positive that the maintainers deliberately accept.** Switching a shipped app from
`Avalonia.Desktop` to explicit `Avalonia.Win32 + Avalonia.Skia + Avalonia.HarfBuzz` is a behavioral
change to the platform/render backend wiring that needs a **full re-test of the `win-x64` publish**.
Until that re-test is scheduled, keeping `Avalonia.Desktop` and accepting the Medium finding is
reasonable. The analyzer's recommendation text now states this tradeoff explicitly.

The corpus tests encode this precisely: a Desktop-referencing project must surface **exactly one**
issue (the accepted Desktop finding) and nothing spurious alongside it — so the analyzer stays
trustworthy as a signal rather than noise.

## Extending the corpus

Add a fixture under `RoslynMcpServer.Tests/Fixtures/Aot/` (authored representations of the relevant
`.csproj`/`.axaml` settings — do **not** copy GPL-licensed source verbatim into this MIT repo) and a
matching assertion in `AotValidationCorpusTests`. Keep each fixture focused on the analyzer-relevant
elements so the expected finding set is obvious.

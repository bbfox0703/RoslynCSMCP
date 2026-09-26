# RoslynCSMCP Agent Skills

Each skill corresponds to an MCP module; load the corresponding module to use it. The skill definitions live in [`Skills-individual/`](Skills-individual/), one file per skill.

---

## Module and Skill Reference Table

| Module | Skills | Tools |
| --- | --- | --- |
| Navigation | [`/roslyn-explore`](Skills-individual/roslyn-explore.md), [`/roslyn-navigate`](Skills-individual/roslyn-navigate.md), [`/roslyn-outline`](Skills-individual/roslyn-outline.md) | 7 |
| Quality | [`/roslyn-quality`](Skills-individual/roslyn-quality.md) | 8 |
| Security | [`/roslyn-security`](Skills-individual/roslyn-security.md) | 3 |
| Dependencies | [`/roslyn-dependencies`](Skills-individual/roslyn-dependencies.md) | 5 |
| Refactoring | [`/roslyn-refactor`](Skills-individual/roslyn-refactor.md) | 5 |
| Testing | [`/roslyn-testing`](Skills-individual/roslyn-testing.md) | 2 |
| Metrics | [`/roslyn-metrics`](Skills-individual/roslyn-metrics.md) | 4 |
| Advanced | [`/roslyn-deep-analysis`](Skills-individual/roslyn-deep-analysis.md), [`/roslyn-batch`](Skills-individual/roslyn-batch.md), [`/roslyn-api-diff`](Skills-individual/roslyn-api-diff.md) | 15 |
| Interop | [`/roslyn-interop`](Skills-individual/roslyn-interop.md) | 3 |
| **Full** | [`/roslyn-full-audit`](Skills-individual/roslyn-full-audit.md) + all above | 51 |

`/roslyn-full-audit` requires the Full version (`RoslynMcpServer`) or all 9 modules.

---

## Installation

### Option 1: Add to CLAUDE.md

Copy the required skill definitions from `Skills-individual/` into the project's `CLAUDE.md`.

### Option 2: Create skill files

Claude Code loads each skill from its own directory, as `SKILL.md`:

```
.claude/
└── skills/
    ├── roslyn-explore/SKILL.md        # Navigation
    ├── roslyn-navigate/SKILL.md       # Navigation
    ├── roslyn-outline/SKILL.md        # Navigation
    ├── roslyn-quality/SKILL.md        # Quality
    ├── roslyn-security/SKILL.md       # Security
    ├── roslyn-dependencies/SKILL.md   # Dependencies
    ├── roslyn-refactor/SKILL.md       # Refactoring
    ├── roslyn-testing/SKILL.md        # Testing
    ├── roslyn-metrics/SKILL.md        # Metrics
    ├── roslyn-deep-analysis/SKILL.md  # Advanced
    ├── roslyn-batch/SKILL.md          # Advanced
    ├── roslyn-api-diff/SKILL.md       # Advanced
    ├── roslyn-interop/SKILL.md        # Interop
    └── roslyn-full-audit/SKILL.md     # Full only
```

Copy `docs/Skills-individual/<name>.md` to `.claude/skills/<name>/SKILL.md`, for example:

```bash
mkdir -p .claude/skills/roslyn-explore
cp docs/Skills-individual/roslyn-explore.md .claude/skills/roslyn-explore/SKILL.md
```

### Select Corresponding Modules

Install only the MCP modules corresponding to the skills:

```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "src/RoslynMcpServer.Navigation"]
    }
  }
}

```

With this configuration, only `/roslyn-explore`, `/roslyn-navigate`, and `/roslyn-outline` will be available.

## Module and Skill Mapping

| Loaded Module | Available Skills |
| --- | --- |
| **Navigation** | `/roslyn-explore`, `/roslyn-navigate`, `/roslyn-outline` |
| **Quality** | `/roslyn-quality` |
| **Security** | `/roslyn-security` |
| **Dependencies** | `/roslyn-dependencies` |
| **Refactoring** | `/roslyn-refactor` |
| **Testing** | `/roslyn-testing` |
| **Metrics** | `/roslyn-metrics` |
| **Advanced** | `/roslyn-deep-analysis`, `/roslyn-batch`, `/roslyn-api-diff` |
| **Interop** | `/roslyn-interop` |
| **Full** | All of the above + `/roslyn-full-audit` |

---

## Usage Guide

For example, if you only load the **Navigation** module:

```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "src/RoslynMcpServer.Navigation"]
    }
  }
}

```

In this scenario, you only need to copy the 3 skill definitions corresponding to **Navigation** into your `CLAUDE.md`. Other skills will not cause errors because the system won't attempt to call tools that aren't defined.

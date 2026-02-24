# Skills Setup

Instructions for installing Claude Code skills used in this project.

## Prerequisites: Install Node.js via Volta

```powershell
# Windows
winget install Volta.Volta
volta install node@24
```

## Install Skills

```bash
npx skills add https://github.com/anthropics/skills --skill skill-creator
npx skills add https://github.com/anthropics/skills --skill mcp-builder
```

## Available Skills

See [`agent-skills.md`](agent-skills.md) for the full list of `/roslyn-*` skills and their usage.

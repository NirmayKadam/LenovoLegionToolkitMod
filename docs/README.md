# Documentation

## Start here

| If you want to… | Read |
|---|---|
| **Use the app** | [USER-GUIDE.md](USER-GUIDE.md): every page, the CLI, startup options, where data lives, troubleshooting |
| **Understand the code from scratch** | [CODEBASE-GUIDE.md](CODEBASE-GUIDE.md): the tech stack in plain words, tools, a reading order, exercises, a worked example, glossary |
| **Look up how something works** | [ARCHITECTURE.md](ARCHITECTURE.md): projects, layers, core patterns, hardware access, startup, data flows, "where to change what" |
| **Build, test or make an installer** | [BUILDING.md](BUILDING.md), [TESTING.md](TESTING.md) |

## What this mod changed

| Document | Contents |
|---|---|
| [CHANGES.md](CHANGES.md) | Dated list of every modified, added and removed file (GPL notice) |
| [SECURITY.md](SECURITY.md) | Threat model, each security fix, remaining limitations |
| [BUG-FIXES.md](BUG-FIXES.md) | Each bug, its cause and its fix |
| [SKINS.md](SKINS.md) | UI styles (Fluent glass, Neon, Retro CRT), animation levels, the dashboard status strip |

## Upstream notes

| Document | Contents |
|---|---|
| [upstream/README.md](upstream/README.md) | The original project's README (also in Japanese, Russian and Chinese) |
| [controls.md](controls.md) | UI control conventions |
| [icons.md](icons.md) | Icon reference |
| [localization.md](localization.md) | How translations work |

## Suggested reading paths

- **"I just want to run it":** USER-GUIDE → BUILDING (installer section).
- **"I want to understand how it works":**
  1. CODEBASE-GUIDE sections 1–3;
  2. ARCHITECTURE up to the core patterns;
  3. CODEBASE-GUIDE sections 4–5 (run it, and follow Fn Lock);
  4. ARCHITECTURE walkthroughs 1 and 2.
- **"I want to change something":** ARCHITECTURE's "Where to change what" table → CODEBASE-GUIDE section 5 → TESTING.

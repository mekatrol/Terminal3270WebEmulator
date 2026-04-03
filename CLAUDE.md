# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A web-based IBM 3270 terminal emulator with a Vue.js 3 frontend (TypeScript) and .NET 10 backend (C# 14). The browser communicates with the server via WSS (WebSocket Secure) using TLS 1.3. Third-party library usage is kept to an absolute minimum.

## Repository Structure

All source code lives under `src/`:

| Path | Description |
|------|-------------|
| `src/Terminal.Api` | ASP.NET Core host process — HTTPS and WSS endpoints |
| `src/Terminal.Common` | Services layer — DI interfaces and protected concrete implementations |
| `src/Terminal.Data` | Entity Framework Code-First persistence (SQL Server) |
| `src/Terminal.Test.Unit` | MSTest unit tests for .NET code |
| `src/terminal.spa` | Vue 3 SPA — terminal emulator for mainframe users |
| `src/admin.spa` | Vue 3 SPA — admin portal for mapping Entra ID users to mainframe users |

## Backend Commands (.NET)

Run from `src/` or any project directory:

```bash
# Build
dotnet build Terminal.slnx

# Run API (HTTP: localhost:5181, HTTPS: localhost:7016)
dotnet run --project Terminal.Api

# Run unit tests
dotnet test Terminal.slnx

# Run a single test class or method
dotnet test Terminal.Test.Unit --filter "ClassName.MethodName"
```

## Frontend Commands (Both SPAs)

Run from `src/admin.spa/` or `src/terminal.spa/`:

```bash
npm install          # Install dependencies
npm run dev          # Dev server
npm run build        # Type-check + production build
npm run test:unit    # Run Vitest unit tests
npm run lint         # Run oxlint then eslint (both auto-fix)
npm run format       # Run prettier formatter
```

Node requirement: 20.19.0+ or 22.12.0+

## Architecture

### Two-Layer Security

1. **Entra ID** (Microsoft MSAL browser-side, .NET auth server-side) — user must hold an OAuth role claim defined in `appsettings.json`
2. **Mainframe credentials** — the server maintains a mapping between Entra ID Object IDs and mainframe usernames (stored in SQL Server) to prevent credential sharing

If a user lacks Entra ID auth or required claims, they are routed to an anonymous-accessible "no permission" page.

### Backend Layering

- **Terminal.Api** — hosts HTTP/WSS endpoints; the only executable project
- **Terminal.Common** — exposes C# interfaces publicly; concrete implementations are `protected` (not accessible outside the library); uses extension methods like `AddXXXServices(...)` for DI registration
- **Terminal.Data** — Entity Framework Code-First; use migrations for schema changes

### Frontend (Both SPAs)

- Vue 3 Composition API with Pinia for state management and Vue Router for navigation
- Vite for dev/build; Vitest (jsdom) for unit tests
- HTTP via axios; WebSocket terminal sessions via WSS

## Code Style

### C# (enforced by `src/.editorconfig`)

- File-scoped namespaces required
- `var` for all type inferences including built-in types
- Unused parameters are compiler errors
- Strict nullable reference types enabled
- Prefer pattern matching over casts and null checks

### TypeScript/Vue (enforced by `eslint.config.ts` and `.prettierrc.json`)

- Single quotes, no semicolons (Prettier), 100-character line width
- Explicit function return types required
- `console` statements disallowed in production code
- Array element newlines enforced by ESLint
- Format on save expected

## Key Requirements

- All UI must be **WCAG 2.2 AA** compliant; use semantic HTML
- Code must be compatible with **Windows and Linux** (AWS and Azure)
- Unit tests are required for all server and SPA functions
- Keep third-party dependencies minimal

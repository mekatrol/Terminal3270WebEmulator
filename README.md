# Terminal3270WebEmulator

Terminal3270WebEmulator is a web-based IBM 3270 terminal emulator. The solution uses a .NET backend to provide secure HTTP and WebSocket endpoints and a Vue 3 single-page application for end users connecting to a mainframe session.

The current design goals and architectural constraints are described in [`design.md`](./design.md).

## Repository Structure

All source code lives under `src/`.

- `src/Terminal.Api` - ASP.NET host for HTTPS and WSS endpoints
- `src/Terminal.Common` - shared service interfaces, protocol logic, and implementations
- `src/Terminal.Data` - data access layer
- `src/Terminal.Console` - console host/client project
- `src/Terminal.Test.Unit` - MSTest unit tests for .NET code
- `src/terminal.spa` - Vue 3 SPA for the terminal emulator
- `src/Terminal.slnx` - .NET solution file

## Prerequisites

Install the following tools before building the repository:

- .NET SDK 10.0 or later
- Node.js `^20.19.0` or `>=22.12.0`
- npm

To verify your environment:

```bash
dotnet --version
node --version
npm --version
```

## Restore Dependencies

Restore .NET packages from the repository root:

```bash
dotnet restore src/Terminal.slnx
```

Install SPA dependencies in the terminal SPA directory:

```bash
cd src/terminal.spa
npm install
```

## Build

### .NET

Build the .NET solution from the repository root:

```bash
dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true
```

### Terminal SPA

```bash
cd src/terminal.spa
npm run build
```

## Format

### .NET

Apply .NET formatting rules to the solution:

```bash
dotnet format src/Terminal.slnx
```

### Terminal SPA

```bash
cd src/terminal.spa
npm run format
```

## Lint

### .NET

This repository enforces .NET code style during build. Run:

```bash
dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true
```

### Terminal SPA

```bash
cd src/terminal.spa
npm run lint
```

## Dependency Health

### .NET

Check the solution for known vulnerable packages:

```bash
dotnet package list src/Terminal.slnx --vulnerable --format json
```

Check the solution for deprecated packages:

```bash
dotnet package list src/Terminal.slnx --deprecated --format json
```

### Terminal SPA

Run the npm vulnerability audit from the SPA directory:

```bash
cd src/terminal.spa
npm run audit
```

## Test

### .NET Unit Tests

Run the MSTest project:

```bash
dotnet test src/Terminal.Test.Unit/Terminal.Test.Unit.csproj
```

### Terminal SPA Unit Tests

```bash
cd src/terminal.spa
npm run test:unit
```

## Recommended Validation Workflow

The design document asks for formatting, compliance, dependency health, and test checks after edits. A practical workflow is:

1. Format .NET and SPA code.
2. Build the .NET solution with code style enforcement enabled.
3. Check .NET packages for vulnerabilities and deprecations.
4. Run lint, build, audit, and unit tests for the terminal SPA.
5. Run the .NET unit tests.

Example command sequence:

```bash
dotnet format src/Terminal.slnx
dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true
dotnet package list src/Terminal.slnx --vulnerable --format json
dotnet package list src/Terminal.slnx --deprecated --format json
dotnet test src/Terminal.Test.Unit/Terminal.Test.Unit.csproj
```

```bash
cd src/terminal.spa
npm run format
npm run lint
npm run build
npm run audit
npm run test:unit
```

## Development Notes

- The .NET projects target `net10.0`.
- The .NET test project uses MSTest.
- The terminal SPA uses Vue 3, TypeScript, Vite, ESLint, Oxlint, Prettier, and Vitest.
- The terminal SPA lint script runs type-checking as part of `npm run lint`.

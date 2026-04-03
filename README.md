# Terminal3270WebEmulator

Terminal3270WebEmulator is a web-based IBM 3270 terminal emulator. The solution uses a .NET backend to provide secure HTTP and WebSocket endpoints and two Vue 3 single-page applications:

- `src/terminal.spa` for end users connecting to a mainframe session
- `src/admin.spa` for administration tasks such as user-to-mainframe mapping

The current design goals and architectural constraints are described in [`design.md`](./design.md).

## Repository Structure

All source code lives under `src/`.

- `src/Terminal.Api` - ASP.NET host for HTTPS and WSS endpoints
- `src/Terminal.Common` - shared service interfaces, protocol logic, and implementations
- `src/Terminal.Data` - data access layer
- `src/Terminal.Console` - console host/client project
- `src/Terminal.Test.Unit` - MSTest unit tests for .NET code
- `src/terminal.spa` - Vue 3 SPA for the terminal emulator
- `src/admin.spa` - Vue 3 SPA for administration
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

Install SPA dependencies in each SPA directory:

```bash
cd src/terminal.spa
npm install
```

```bash
cd src/admin.spa
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

### Admin SPA

```bash
cd src/admin.spa
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

### Admin SPA

```bash
cd src/admin.spa
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

### Admin SPA

```bash
cd src/admin.spa
npm run lint
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

### Admin SPA Unit Tests

```bash
cd src/admin.spa
npm run test:unit
```

## Recommended Validation Workflow

The design document asks for formatting, compliance, and test checks after edits. A practical workflow is:

1. Format .NET and SPA code.
2. Build the .NET solution with code style enforcement enabled.
3. Run lint, build, and unit tests for each SPA.
4. Run the .NET unit tests.

Example command sequence:

```bash
dotnet format src/Terminal.slnx
dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true
dotnet test src/Terminal.Test.Unit/Terminal.Test.Unit.csproj
```

```bash
cd src/terminal.spa
npm run format
npm run lint
npm run build
npm run test:unit
```

```bash
cd src/admin.spa
npm run format
npm run lint
npm run build
npm run test:unit
```

## Development Notes

- The .NET projects target `net10.0`.
- The .NET test project uses MSTest.
- Both SPAs use Vue 3, TypeScript, Vite, ESLint, Oxlint, Prettier, and Vitest.
- The terminal SPA lint script runs type-checking as part of `npm run lint`.
- The admin SPA exposes `type-check` separately, while `npm run build` also performs type-checking before bundling.

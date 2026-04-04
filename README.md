# Terminal3270WebEmulator

Terminal3270WebEmulator is a web-based IBM 3270 terminal emulator. The solution uses a .NET backend to provide secure HTTP and WebSocket endpoints and a Vue 3 single-page application for end users connecting to a mainframe session.

The current design goals and architectural constraints are described in [`design.md`](./design.md).

For local development the repository also includes a mock identity provider inside `Terminal.MockServer`. It exposes an OpenID Connect and OAuth 2.0 authorization code flow with PKCE so the SPA and API can be exercised without a real Microsoft Entra ID tenant or another external identity provider.

## Repository Structure

All source code lives under `src/`.

- `src/Terminal.Api` - ASP.NET host for HTTPS and WSS endpoints
- `src/Terminal.Common` - shared service interfaces, protocol logic, and implementations
- `src/Terminal.Data` - data access layer
- `src/Terminal.Console` - console host/client project
- `src/Terminal.MockServer` - mock TN3270 host plus mock OpenID Connect identity provider
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

## Running the development environment

The typical local development setup uses three processes:

1. `Terminal.MockServer` for the mock TN3270 endpoint and mock identity provider
2. `Terminal.Api` for the protected API and WebSocket proxy
3. `terminal.spa` for the browser client

Example startup sequence:

```bash
dotnet run --project src/Terminal.MockServer
```

```bash
dotnet run --project src/Terminal.Api
```

```bash
cd src/terminal.spa
npm run dev
```

The default mock identity issuer is `http://localhost:5099/mock-entra/terminaltenant/v2.0`.

## Mock identity provider

`Terminal.MockServer` now hosts a lightweight OpenID Connect provider for development and test scenarios. The implementation is intended to feel similar to Microsoft Entra ID while remaining standards-based enough for other OAuth-compatible clients.

The mock provider currently exposes:

- discovery metadata
- JWKS signing keys
- authorization endpoint
- token endpoint
- userinfo endpoint
- logout endpoint
- a simple HTML sign-in page for the authorization code flow

The SPA uses the authorization code flow with PKCE. `Terminal.Api` validates the resulting bearer access tokens and authorizes requests using the `roles` claim in those tokens.

## Configuring mock users

Mock users are configured in [`src/Terminal.MockServer/appsettings.json`](./src/Terminal.MockServer/appsettings.json) under `MockIdentity:Users`.

Each mock user supports:

- `SubjectId` - stable OpenID Connect `sub` claim
- `ObjectId` - Entra-style `oid` claim
- `UserName` - sign-in name and `preferred_username`
- `DisplayName` - `name` claim
- `Email` - `email` claim
- `Password` - password accepted by the mock login page
- `Roles` - values emitted into the `roles` claim for API and SPA authorization

Current default mock users are:

- `operator@mockidp.local` / `Passw0rd!` with `Terminal.User`, `Terminal.Admin`
- `viewer@mockidp.local` / `Passw0rd!` with `Terminal.User`
- `serveradmin@mockidp.local` / `Passw0rd!` with `Server.Admin`

To add another mock user, append a new entry to `MockIdentity:Users`. Example:

```json
{
  "SubjectId": "44444444444444444444444444444444",
  "ObjectId": "44444444-4444-4444-4444-444444444444",
  "UserName": "helpdesk@mockidp.local",
  "DisplayName": "Mock Helpdesk",
  "Email": "helpdesk@mockidp.local",
  "Password": "Passw0rd!",
  "Roles": [
    "Terminal.User"
  ]
}
```

After changing the mock-user list, restart `Terminal.MockServer` so the new configuration is loaded.

## Authorization roles

The local development setup currently uses these role names:

- `Terminal.User` for terminal access
- `Terminal.Admin` for terminal-oriented elevated access claims
- `Server.Admin` for the SPA `/admin` area and protected admin APIs

The admin SPA route and the `Terminal.Api` admin endpoints require `Server.Admin`.

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
dotnet package list --project src/Terminal.slnx --vulnerable --format json
```

Check the solution for deprecated packages:

```bash
dotnet package list --project src/Terminal.slnx --deprecated --format json
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
dotnet package list --project src/Terminal.slnx --vulnerable --format json
dotnet package list --project src/Terminal.slnx --deprecated --format json
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
- `Terminal.MockServer` is both a TN3270 test host and a mock OpenID Connect identity provider for local development.

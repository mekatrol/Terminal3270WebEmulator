# Design

## Purpose and overview

This application provides a web-based terminal emulator for the IBM 3270 protocol. It is intended to provide secure, robust access to a mainframe from a web browser.

The application is expected to support standard TN3270 and TN3270E protocol behavior, including server-provided color and presentation attributes, consistent with RFC 1576 (TN3270 Current Practices, https://www.rfc-editor.org/rfc/rfc1576) and RFC 2355 (TN3270 Enhancements, https://www.rfc-editor.org/rfc/rfc2355).

## Technology stack

### Browser side

The browser client is a Vue 3 single-page application (SPA) coded in TypeScript and using the Composition API.

The browser communicates with the server over HTTPS for standard web requests and over WSS for terminal session traffic.

Third-party library usage should be kept to an absolute minimum. The main browser-side libraries are:

- Vue
- TypeScript typings required for development

Vite is used for local development and production packaging.

### Server side

The server side is developed in modern .NET using .NET 10, TLS, and C# 14.

Third-party library usage should be kept to an absolute minimum. The primary server-side libraries should be the .NET platform itself together with Entity Framework.

### Data persistence

Data is stored in memory using the Entity Framework Core InMemory provider.

Entity Framework Core is used for data access, with the InMemory provider used for runtime storage.

## Security

### Authentication

Authentication occurs at two layers:

1. OAuth-based authentication in the browser and .NET authentication on the server
2. Mainframe authentication, using user name and password

A user must be authenticated and authorized through the configured OAuth provider and the mainframe to access the terminal session.

When a user opens the terminal emulator, the first step is to ensure the user is authenticated through the configured OAuth provider and holds at least one of the role claims defined in the .NET application configuration.

If the user is not authenticated or does not hold the required claims, the user is directed to a route that is accessible to anonymous users and explains that access is not permitted.

If the user does hold the required claims, the user is directed to the mainframe login page to enter mainframe credentials.

For local development, the repository includes a mock identity provider hosted by `Terminal.MockServer`. The SPA uses the OpenID Connect authorization code flow with PKCE against that provider, and `Terminal.Api` validates bearer access tokens issued by it. The provider emits standard claims such as `sub`, `iss`, `aud`, and `email`, together with Entra-style claims such as `oid`, `tid`, `preferred_username`, and `roles`.

### Authorization

OAuth role claims are used to authorize access to the application. Mainframe permissions and roles then determine what the authenticated user is allowed to do within the mainframe session itself.

OAuth establishes that the user is permitted to access the application in a specific role. The mainframe remains responsible for terminal-level permissions and behavior.

The current logical role split is:

- `Terminal.User` for browser access to the terminal session
- `Terminal.Admin` for elevated terminal-related identity semantics when needed by downstream application behavior
- `Server.Admin` for access to the administrative SPA route space and protected administrative HTTP endpoints

### Protocols

The browser uses HTTPS for page loads, authentication-related operations, session management functions, and similar application requests.

The browser uses WSS for the live terminal session between the SPA and the .NET server.

Both protocols use TLS 1.3.

## Architecture

The system consists of a single Vue 3 SPA connected to a .NET server, with a separate mock host used during development.

The SPA is responsible for the browser user interface, authentication flow, and terminal session experience. Initial application requests use HTTPS. Interactive terminal session traffic uses WSS so the browser and server can maintain a real-time connection.

The user-facing application provides the following primary capabilities:

- Mainframe terminal emulation for authenticated users.
- Session tracking, including visibility of open sessions and the ability to terminate a session when the design requires that behavior.

The .NET server is divided into the following layers:

1. Hosting layer - provides the HTTPS and WSS endpoints. This is the host process.
2. Services layer - the dependency-injected services layer that performs server functions. It is a .NET library that exposes C# interfaces for operations. Concrete implementation classes should be marked `internal` so they are not accessible outside the library. Extension methods such as `AddTerminalServices` should be used to register functionality with the host using standard .NET dependency injection patterns.
3. Persistence layer - the Entity Framework Core data access layer, configured to use the InMemory provider for application data.

For local development and automated validation, `Terminal.MockServer` complements `Terminal.Api` with two separate responsibilities:

1. A mock TN3270 or TN3270E endpoint that returns deterministic screens and accepts known sign-in credentials.
2. A mock OpenID Connect identity provider that supports the browser and API authentication flow without depending on an external tenant.

### Mock identity provider design

The mock identity provider is intentionally lightweight and configuration-driven rather than a full identity platform. Its design goals are:

- keep local development self-contained
- produce realistic token and claim shapes for the SPA and API
- exercise authorization code plus PKCE behavior instead of bypassing login
- allow user and role changes through configuration only

The provider is hosted inside `Terminal.MockServer` so a single development process can supply both the mock terminal host and the identity system needed to reach it.

The provider exposes these endpoint categories:

- OpenID Connect discovery metadata
- JWKS signing keys
- authorization endpoint with a simple HTML login page
- token endpoint for authorization code and refresh token exchange
- userinfo endpoint
- logout endpoint

Token issuance is backed by an in-memory store of authorization codes, refresh tokens, and access-token metadata. Signing keys are generated at runtime, which is acceptable for local development because the SPA and API discover them dynamically through the metadata and JWKS endpoints.

Mock users and clients are defined in configuration. A mock user includes identifiers, display attributes, password, and a set of roles. Those configured roles are emitted into the `roles` claim in access tokens and ID tokens, and are also returned from the userinfo endpoint.

The provider uses an Entra-inspired path structure such as `/mock-entra/{tenant}/oauth2/v2.0/authorize` and `/mock-entra/{tenant}/oauth2/v2.0/token`, but the actual protocol behavior is intentionally standards-oriented so the implementation can model other OAuth-compatible identity providers as well.

### Runtime composition

In the current development architecture:

1. The SPA redirects the browser to the mock identity provider for sign-in.
2. The identity provider returns an authorization code to the SPA callback route.
3. The SPA redeems the code for tokens and stores the resulting browser session state.
4. The SPA sends the bearer access token to `Terminal.Api` over HTTPS and WSS.
5. `Terminal.Api` validates the token against the configured authority and authorizes requests based on the `roles` claim.
6. `Terminal.Api` then proxies TN3270 or TN3270E traffic to the TN3270 mock host when a terminal session is established.

Unit tests are required for both server-side code and SPA code.

## Folder structure

`src` contains all source code.

`src/Terminal.Api` is the HTTP and WSS host process  
`src/Terminal.Console` contains the console-based TN3270 or TN3270E client application  
`src/Terminal.Common` contains service interfaces, concrete service implementations, and dependency injection helper extensions  
`src/Terminal.Data` contains the Entity Framework Core persistence layer  
`src/Terminal.MockServer` contains a mock terminal server used for development and testing  
`src/Terminal.MockServer/Auth` contains the mock OpenID Connect provider implementation and configuration-bound identity model  
`src/terminal.spa` contains the Vue SPA for the terminal website  
`src/Terminal.Test.Unit` contains .NET unit tests

`src/Terminal.Api` is the executable host project.  
`src/Terminal.Console` is an executable .NET console project used for TN3270 or TN3270E client connectivity and related testing or debugging scenarios.  
`src/Terminal.Common` is a .NET library.    
`src/Terminal.Data` is a .NET library.  
`src/Terminal.MockServer` is an executable .NET project that provides a mock TN3270 or TN3270E server and a mock OpenID Connect identity provider for development and testing.  

## Technical requirements

### Unit tests

MSTest is the Microsoft test framework used as the .NET test runner.

### WCAG compliance and UX themes and styles

User interface code and components must be WCAG 2.2 AA compliant. Semantic HTML should be used.

Solid colors are preferred over gradiant colors (don't use gradient colots in theming)

Use vue js code instead of semantic tages. eg an <a> HTML tag should not be used, a vue js <RouterLink> tag should be used instead.

### Host platforms

All code should be compatible with both Windows and Linux, whether deployed on AWS or Azure.

## Code style

Code style and linting rules are defined by:

`.editorconfig` for server-side C#  
`eslint.config.ts` for SPA TypeScript  
`.prettierrc.json` for SPA TypeScript

Code should be formatted on save.

All C# methods should accept cancellation tokens and use them correctly so that the application can scale to thousands of concurrent users.

### Exception handling

Exceptions should never be swallowed without logging unless there is a clear and explicit decision explaining why the exception is intentionally being swallowed.

### Comments

I want verbose documentation comments.

Comments should describe not only what the code is doing, but also why the code is doing it. Where possible, comments should cite authoritative specifications or documentation with URLs such as RFC 1576 (https://www.rfc-editor.org/rfc/rfc1576) and RFC 2355 (https://www.rfc-editor.org/rfc/rfc2355).

## Code build after edit

### C#

Always run the following commands to ensure formatting, rule compliance, package vulnerability detection, and package deprecation detection:

`dotnet format src/Terminal.slnx`

`dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true`

`dotnet package list --project src/Terminal.slnx --vulnerable --format json`

`dotnet package list --project src/Terminal.slnx --deprecated --format json`

### TypeScript / Vue / SPA

Always run the following commands to ensure SPA formatting, compliance, and package vulnerability detection:

```bash
npm run format
npm run lint
npm run build
npm run audit
```

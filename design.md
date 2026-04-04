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
- An OAuth-compatible browser authentication library and the corresponding .NET authentication components
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

### Authorization

OAuth role claims are used to authorize access to the application. Mainframe permissions and roles then determine what the authenticated user is allowed to do within the mainframe session itself.

OAuth establishes that the user is permitted to access the application in a specific role. The mainframe remains responsible for terminal-level permissions and behavior.

### Protocols

The browser uses HTTPS for page loads, authentication-related operations, session management functions, and similar application requests.

The browser uses WSS for the live terminal session between the SPA and the .NET server.

Both protocols use TLS 1.3.

## Architecture

The system consists of a single Vue 3 SPA connected to a .NET server.

The SPA is responsible for the browser user interface, authentication flow, and terminal session experience. Initial application requests use HTTPS. Interactive terminal session traffic uses WSS so the browser and server can maintain a real-time connection.

The user-facing application provides the following primary capabilities:

- Mainframe terminal emulation for authenticated users.
- Session tracking, including visibility of open sessions and the ability to terminate a session when the design requires that behavior.

The .NET server is divided into the following layers:

1. Hosting layer - provides the HTTPS and WSS endpoints. This is the host process.
2. Services layer - the dependency-injected services layer that performs server functions. It is a .NET library that exposes C# interfaces for operations. Concrete implementation classes should be marked `internal` so they are not accessible outside the library. Extension methods such as `AddTerminalServices` should be used to register functionality with the host using standard .NET dependency injection patterns.
3. Persistence layer - the Entity Framework Core data access layer, configured to use the InMemory provider for application data.

Unit tests are required for both server-side code and SPA code.

## Folder structure

`src` contains all source code.

`src/Terminal.Api` is the HTTP and WSS host process  
`src/Terminal.Console` contains the console-based TN3270 or TN3270E client application  
`src/Terminal.Common` contains service interfaces, concrete service implementations, and dependency injection helper extensions  
`src/Terminal.Data` contains the Entity Framework Core persistence layer  
`src/Terminal.MockServer` contains a mock terminal server used for development and testing  
`src/terminal.spa` contains the Vue SPA for the terminal website  
`src/Terminal.Test.Unit` contains .NET unit tests

`src/Terminal.Api` is the executable host project.  
`src/Terminal.Console` is an executable .NET console project used for TN3270 or TN3270E client connectivity and related testing or debugging scenarios.  
`src/Terminal.Common` is a .NET library.    
`src/Terminal.Data` is a .NET library.  
`src/Terminal.MockServer` is an executable .NET project that provides a mock TN3270 or TN3270E server for development and testing.  

## Technical requirements

### Unit tests

MSTest is the Microsoft test framework used as the .NET test runner.

### WCAG compliance

User interface code and components must be WCAG 2.2 AA compliant. Semantic HTML should be used.

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

Always run the following commands to ensure formatting and rule compliance:

`dotnet format src/Terminal.slnx`

`dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true`

### TypeScript / Vue / SPA

Always run the following commands to ensure SPA compliance:

```bash
npm run format
npm run lint
npm run build
```

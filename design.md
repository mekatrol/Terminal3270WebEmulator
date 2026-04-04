#

## Purpose and overview

This application provides a web-based terminal emulator for the IBM 3270 protocol. It supports all 3270 protocol features, including color codes sent by the server. It is intended to allow secure and robust connection to a mainframe from a web browser.

## Technology stack

### Browser side

The browser-side technology is a Vue 3 SPA (single-page application) coded in TypeScript and using the Composition API. Connectivity to the server uses the WSS (WebSocket Secure) protocol. Third-party library usage is kept to an absolute minimum, with the primary libraries being Vue, Microsoft MSAL for authentication and authorization, and TypeScript typings for development. Vite is used for development and packaging.

### Server side

The server side is developed in modern .NET using .NET 10, TLS, and C# 14. Third-party library usage is kept to an absolute minimum, with the primary libraries being the .NET stack itself along with Entity Framework.

### Data persistence

Data is persisted to Microsoft SQL Server. The ORM is Entity Framework using a code-first approach.

## Security

### Authentication

Authentication occurs at two layers:

1. Entra ID, using Microsoft MSAL in the browser and .NET authentication on the server
2. Mainframe, using user name and password

This means a user must be authenticated and authorized in both Entra ID and the mainframe to access the mainframe terminal.

When the user hits the URL for the terminal emulator, the first step is to ensure the user is authenticated via Entra ID and holds at least one of the role claims defined in the app configuration for the .NET application.

If they do not have authentication or the required claims, they are directed to a route that is accessible for anonymous users and provides a message explaining that they do not have permission to access the site.

If they do have the appropriate claims, they are directed to the mainframe login page where they must enter their mainframe credentials. The .NET server maintains a mapping between Entra ID object IDs and the mainframe user name so that one user cannot access the website and then have another user sign in to the mainframe. The Entra ID user must be mapped to the mainframe user configured for their profile in the server SQL database.

### Authorization

OAuth role claims are used to authorize a user, along with mainframe permissions and roles for the specific user. OAuth identifies that the user is a mainframe user in a particular mainframe role, and then the mainframe itself controls the specific permissions for that user.

### Protocols

The browser loads pages, authorizes users, and performs similar operations using the HTTP protocol and the Axios TypeScript library. The browser also communicates with the server using WSS for a terminal session. Both protocols use TLS 1.3.

## Architecture

There are two websites:

1. The mainframe terminal emulator, for users to connect to a mainframe terminal session
2. The administration site, for administrators to map Entra ID users (object ID) to mainframe users (user name). This site also allows specifying which mainframe role claims a user has, potentially multiple claims for granular authorization.

Each website is its own Vue 3 SPA.

The .NET server is broken into the following layers:

1. Hosting layer. This layer provides the HTTPS and WSS endpoints. This is the host process.
2. Services layer. This is the dependency-injected services layer that performs server functions. It is a .NET library that exposes C# interfaces for operations. Concrete classes for implementation are marked `internal` so they are not accessible outside the library. Extension classes are used to inject services. Public classes should follow standard C# DI patterns. For example, extension methods such as `AddXxxServices` should be used to register specific functionality with the host.
3. Persistence layer. This is the .NET Entity Framework code-first layer, including migrations and related functionality.

The solution should also have unit tests for all server and SPA functions.

## Code style

Code style and linting rules are set by:

`.editorconfig` for server-side C# coding  
`eslint.config.ts` for SPA TypeScript coding  
`.prettierrc.json` for SPA TypeScript coding

Code should be formatted on save.

All C# methods should accept cancellation tokens and use the token correctly so that the application can scale to thousands of concurrent users.

### Exception handling

Exceptions should never be swallowed without logging unless there is a clear, explicit decision explaining why the exception is being swallowed.

### Comments

I want verbose documentation comments. Comments should not only describe what the code is doing, but also why the code is doing it. Comments should also cite authoritative sources and specifications with URLs where possible.

## Code build after edit

### C#

Always run the following to ensure rules and formatting compliance:

`dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true`

and

`dotnet format src/Terminal.slnx`

### TypeScript / Vue / SPA

Always run the following to ensure code compliance:

```bash
npm run format
npm run lint
npm run build
```

## Folder structure

`src` contains all source code.

`src/Terminal.Api` is the HTTP / WSS host process  
`src/Terminal.Common` contains the concrete classes and interface definitions for services, along with extension helper classes for DI  
`src/Terminal.Data` contains the Entity Framework layer  
`src/terminal.spa` contains the Vue SPA for the terminal website  
`src/admin.spa` contains the Vue SPA for the administration website  
`src/Terminal.Test.Unit` contains .NET unit tests

`src/Terminal.Api` is the host process library  
`src/Terminal.Common` is a .NET library  
`src/Terminal.Data` is a .NET library

# Unit tests

MSTest is the Microsoft testing framework used as the test runner.

## WCAG compliance

User interface code and components must be WCAG 2.2 AA compliant. Semantic HTML should be used.

## Host platforms

All code should be compatible with both Windows and Linux on either AWS or Azure.

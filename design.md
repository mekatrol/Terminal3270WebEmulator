#

## Purpose & overview

This application provides a web based terminal emulator for the IBM 3270 protocol. It supports all 3270 protocol features including color codes from server. It is intended to allow connection to a mainframe from a web browser in a secure and robust way.

## Technology stack

### Browser side

The browser side technology is Vue JS 3 SPA (single page application) coded in TypeScript and using the composition API. Connectivity to the server uses the WSS (WebSocket Secure) protocol to talk to the server. Third party library usage is kept to an absolute minium with primary ones being vuejs, Miccrosoft MSAL for autehtnication/authorisation, TypeScript types for development. Vite is used for development and packaging.

### Server Side

The server side is developed in moden .NET using .NET 10 TLS and written in C# 14. Third party library usage is kept to an absolute minium with primary ones being the .NET core stack itself along with entity framework.

### Data persistence

Data is persisted to Microsoft SQL server. The ORM is entity framework using code first approach.

## Security

### Authentication

Authentication occurs at two layers:

1. Entra ID  - using Microsoft MSAL broser side and .NET auth server side
2. Mainframe - user name and password.

This means a user must be authentication and authorised both in Entra ID and in the mainframe to access the mainframe terminal.

When the user hits the URL for the terminal emulator the first step is to ensure the user is is authenticated via Entra ID and they must hold any one of the role claims defined in the app.config file for the .NET app.

If they do not  have authentication or required claims they are directed to a route that is accessible for anonymous users providing a message they do not have permission to access the site.

If they do have appropriate claims then they are directed to the mainframe login page where they must enter their mainframe credential. The .NET server maintains a mapping between Entria ID object IDs and the mainframe mainframe user name so that one user cannot access the website and then another user signinto the mainfram. The Entra ID user must be the mainframe user mapped to their profile/account configuration in the server SQL database.

### Authorisation

oAuth role claims are used to authorise a user as well as mainframe permissions and roles for the specific user. oAuth identifies they are a mainframe user in a particilar mainframe role and then the mainframe itself control specific permissions for that user.

### Protocols

The browser loads pages, authorises users and so forth using HTTP protocol and the axios TypeScript library. The browser also communicates to the server using WSS for a terminal session. Both protocols use TLS 1.3.

## Architecure

There are two web sites:

1. The mainframe terminal emulator - for users to connect to a mainframe terminal session. 
2. The administration server - for administrators to map Entra ID users (Object ID) to mainframe users (user name). This site allows specifying which mainframe role claim a user has (can be multiple for role granularity)

Each website is its own Vue JS 3 SPA.

The .NET server is broken into the following layers:

1. Hosting layer - the layer that provides the HTTPS and WSS end points. This is the host process.
2. Services layer - the dependency injectable services that perform server functions. This is a .NET library that exposes C# interfaces for operations. Concrete classes for implmentation which are marked internal so that are not accessible outside the library. Extension classes are used to inject services. There are public classes following the C# DI patterns. For example an extension like 'AddXXXServices to add specific function / operations to the host.
3. Persistenece layer - is .NET Entity Framework code first using migrations and the like.

The solution should also have unit tests for all server and SPA functions.

## Code Style

Code style and linting rules are set by:

.editorconfig for server C# coding 
eslint.config.ts for SPA TypeScript coding
.prettierrc.json for SPA TypeScript coding

Code should be formatted on save

All C# methods should pass cancellation tokens and use the token so that scalability can be achieved for 1000s of concurrent users

### Exception handling

Exceptions should never be swallowed without loggin unless there is a clear descition of why exception is being swallowed

### Comments

I want to verbosely documented comments. Comments should not only describe what the code is doing, but also why the code is doing something. Comments should also cite authoritative sources and specifications with URLs if possible.

## Code build after edit

### C#
Allways run to ensure rules and formatting:
`dotnet build src/Terminal.slnx /p:EnforceCodeStyleInBuild=true`
and 
`dotnet format src/Terminal.slnx`

### Typescript/Vue JS/SPA

Always run to ensure code compliance
```bash
npm run format
npm run lint
npm run build
```


## Folder structure

src - all soruce code resides under this folder
src/Terminal.Api        - the HTTP / ESS host process
src/Terminal.Common     - The concrete classes and interface definitions for services along with extension helper classes for DI of services
src/Terminal.Data       - The entity framework layer 
src/terminal.spa        - The vue js SPA for the terminal website
src/admin.spa           - The vue js SPA for the terminal website
src/Terminal.Test.Unit  - for .NET code bas unit tests

src/Terminal.Api    - is the host process library
src/Terminal.Common - is a .NET library
src/Terminal.Data   - is a .NET library

# Unit tests

mstest is the Microsoft testing framework used for test runner

## WCAG compliance

User interface code and components must be 2.2 should be WCAG AA compliant.
Semantic HTML should be used

## Host platforms

All cose should be compatible with both Windows and Linux on aither AWS or Azure


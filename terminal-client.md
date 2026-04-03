I am going to add a telnet client for TN3270E protocol communication.

Read design.md for guidance and rules

The implementation is in phases:

1. Add a new .NET console project to the solution named Terminal.Console. It should use the .NET code app host pattern with dependency injection, appsettings.json and appsetting.Development.json patterns. It will draw its services from Terminal.Common project using the AddTerminalServices method.
2. Create an ITn3270EService interface under Terminal.Common/Services
3. Create an Tn3270EService class under Terminal.Common/Services that implements the ITn3270EService interface
4. Add methods to support connection to a server via Telnet over TCP and estanblish a TN3270E terminal session. These needs to be structured to allow unit testing at a granular level
5. The console app should inject these terminal services and pass configuration from appsettings files
6. Create unit tests for the services and methods

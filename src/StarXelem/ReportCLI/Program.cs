using Spectre.Console.Cli;
using StarXelem.ReportCLI.Commands;

var app = new CommandApp<CompareCommand>();
return app.Run(args);

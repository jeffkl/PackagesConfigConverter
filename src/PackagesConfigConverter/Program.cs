// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CommandLine;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace PackagesConfigConverter
{
    internal class Program
    {
        public static int Main(string[] args)
        {
            int ret = 0;

            try
            {
                new Parser(parserSettings =>
                    {
                        parserSettings.CaseInsensitiveEnumValues = true;
                        parserSettings.CaseSensitive = true;
                        parserSettings.EnableDashDash = true;
                        parserSettings.HelpWriter = Console.Out;
                    })
                    .ParseArguments<ProgramArguments>(args)
                    .WithParsed(Run)
                    .WithNotParsed(_ => ret = 1);
            }
            catch (OperationCanceledException)
            {
                ret = -1;
            }
            catch (Exception e)
            {
                ret = 2;

                Log.Error(e.ToString());

                return ret;
            }

            return ret;
        }

        public static void Run(ProgramArguments arguments)
        {
            if (arguments.Debug)
            {
                Debugger.Launch();
            }

            ConfigureLogger(arguments);

            using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddSerilog());
            ILogger logger = factory.CreateLogger("PackagesConfigConverter");

            ProjectConverterSettings settings = new ProjectConverterSettings
            {
                RepositoryRoot = arguments.RepoRoot,
                Include = arguments.Include.ToRegex(),
                Exclude = arguments.Exclude.ToRegex(),
                Log = logger,
                TrimPackages = arguments.Trim,
            };

            if (arguments.DefaultTargetFramework != null)
            {
                settings.DefaultTargetFramework = NuGetFramework.Parse(arguments.DefaultTargetFramework);
            }

            logger.LogInformation($" RepositoryRoot: '{settings.RepositoryRoot}'");
            logger.LogInformation($"  Include regex: '{settings.Include}'");
            logger.LogInformation($"  Exclude regex: '{settings.Exclude}'");
            logger.LogInformation(string.Empty);

            if (!arguments.Yes)
            {
                Console.Write("Ensure there are no files checked out in git before continuing!  Continue? (Y/N) ");
                if (!Console.In.ReadLine()!.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OperationCanceledException();
                }
            }

            var cancellationTokenSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = false;

                logger.LogInformation("Cancelling...");

                cancellationTokenSource.Cancel();
            };

            using IProjectConverter projectConverter = ProjectConverterFactory.Create(settings);

            projectConverter.ConvertRepository(cancellationTokenSource.Token);
        }

        private static void ConfigureLogger(ProgramArguments arguments)
        {
            var loggingConfiguration = new LoggerConfiguration();

            loggingConfiguration.MinimumLevel.Is(arguments.Verbose ? LogEventLevel.Verbose : LogEventLevel.Debug);

            // Always write to the console.
            LogEventLevel consoleLogLevel = arguments.Verbose ? LogEventLevel.Verbose : LogEventLevel.Information;
            loggingConfiguration.WriteTo.Console(consoleLogLevel);

            if (arguments.LogFile != null)
            {
                string logFile = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(arguments.LogFile));
                if (File.Exists(logFile))
                {
                    File.Delete(logFile);
                }

                LogEventLevel fileLogLevel = arguments.Verbose ? LogEventLevel.Verbose : LogEventLevel.Debug;
                loggingConfiguration.WriteTo.Async(a => a.File(logFile, fileLogLevel));
            }

            Log.Logger = loggingConfiguration.CreateLogger();
        }
    }
}
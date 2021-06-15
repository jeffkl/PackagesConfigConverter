// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using CommandLine;
using log4net;
using log4net.Appender;
using log4net.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PackagesConfigProjectConverter
{
    internal class Program
    {
        private static readonly CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

        private static readonly ILog Log = LogManager.GetLogger(typeof(Program));

        public static int Main(string[] args)
        {
            int ret = 0;

            try
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = false;

                    Log.Info("Cancelling...");

                    CancellationTokenSource.Cancel();
                };

                new Parser(parserSettings =>
                    {
                        parserSettings.CaseInsensitiveEnumValues = true;
                        parserSettings.CaseSensitive = true;
                        parserSettings.EnableDashDash = true;
                        parserSettings.HelpWriter = Console.Out;
                    })
                    .ParseArguments<ProgramArguments>(args)
                    .WithParsed(Run)
                    .WithNotParsed(errors => ret = 1);
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

            ProjectConverterSettings settings = new ProjectConverterSettings
            {
                RepositoryRoot = arguments.RepoRoot,
                Include = arguments.Include.ToRegex(),
                Exclude = arguments.Exclude.ToRegex(),
                Log = Log,
                TrimPackages = arguments.Trim,
            };

            Log.Info($" RepositoryRoot: '{settings.RepositoryRoot}'");
            Log.Info($"  Include regex: '{settings.Include}'");
            Log.Info($"  Exclude regex: '{settings.Exclude}'");
            Log.Info(string.Empty);

            if (!arguments.Yes)
            {
                Console.Write("Ensure there are no files checked out in git before continuing!  Continue? (Y/N) ");
                if (!Console.In.ReadLine().StartsWith("Y", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OperationCanceledException();
                }
            }

            using (IProjectConverter projectConverter = ProjectConverterFactory.Create(settings))
            {
                projectConverter.ConvertRepository(CancellationTokenSource.Token);
            }
        }

        private static void ConfigureLogger(ProgramArguments arguments)
        {
            foreach (AppenderSkeleton appender in Log.Logger.Repository.GetAppenders().OfType<AppenderSkeleton>())
            {
                switch (appender)
                {
                    case FileAppender fileAppender:
                        if (arguments.LogFile != null)
                        {
                            fileAppender.File = Path.Combine(Environment.CurrentDirectory, Path.GetFileName(arguments.LogFile));
                        }
                        else
                        {
                            fileAppender.Threshold = Level.Off;
                        }

                        fileAppender.ActivateOptions();

                        break;
                }

                if (arguments.Verbose)
                {
                    appender.Threshold = Level.Verbose;

                    appender.ActivateOptions();
                }
            }
        }
    }
}
using CmdLine;
using Microsoft.Build.Evaluation;
using NuGet.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ConsoleApp
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Any(i => i.Equals("/?", StringComparison.OrdinalIgnoreCase)))
                {
                    return PrintUsage();
                }

                ProgramArguments arguments = CommandLine.Parse<ProgramArguments>();

                if (arguments.Debug)
                {
                    Debugger.Launch();
                }

                if (String.IsNullOrWhiteSpace(arguments.RepoRoot))
                {
                    return PrintUsage("You must specify a repository path");
                }

                Console.WriteLine($" EnlistmentRoot: '{arguments.RepoRoot}'");
                Console.WriteLine($"  Exclude regex: '{arguments.Exclude}'");
                Console.WriteLine($"  Include regex: '{arguments.Include}'");
                Console.WriteLine();

                if (!arguments.Quiet)
                {
                    Console.Write("Ensure there are no files checked out in git before continuing!  Continue? (Y/N) ");
                    if (!Console.ReadLine().StartsWith("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }
                }

                using (ProjectConverter projectConverter = new ProjectConverter(new ProjectCollection(), GetPackageRootPath(arguments.RepoRoot), arguments.RepoRoot, arguments.UsePackagesProps))
                {
                    Console.WriteLine("Converting...");

                    projectConverter.ConvertRepository(arguments.RepoRoot, arguments.Exclude, arguments.Include);

                    Console.WriteLine("Success!");
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());

                return 1;
            }

            return 0;
        }

        private static int PrintUsage(string errorMessage = null)
        {
            if (!String.IsNullOrWhiteSpace(errorMessage))
            {
                Console.Error.WriteLine(errorMessage);
            }

            Console.WriteLine("Converts a repository from packages.config to PackageReference.");
            Console.WriteLine();

            Console.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name}.exe repositoryPath [/quiet] [/exclude:path1;path2]");
            Console.WriteLine();
            Console.WriteLine("    /RepoRoot          Full path to the repository root to convert");
            Console.WriteLine("    /quiet             Do not prompt before converting the tree");
            Console.WriteLine("    /exclude           Regex for project files to exclude");
            Console.WriteLine("    /include           Regex for project files to include");
            Console.WriteLine("    /debug             Launch the debugger before executing");
            Console.WriteLine("    /UsePackagesProps  Update packages.props in the root of the repo");

            return String.IsNullOrWhiteSpace(errorMessage) ? 0 : 1;
        }

        private static string GetPackageRootPath(string repoRoot)
        {
            string nuGetConfigPath = Path.Combine(repoRoot, Settings.DefaultSettingsFileName);

            if (File.Exists(nuGetConfigPath))
            {
                ISettings settings = Settings.LoadDefaultSettings(repoRoot, Settings.DefaultSettingsFileName, new XPlatMachineWideSetting());

                return SettingsUtility.GetRepositoryPath(settings);
            }

            throw new InvalidOperationException($"{nuGetConfigPath} doesn't exist");
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ConsoleApp
{
    internal class Program
    {
        // @"D:\Truman2\private\Libraries\ServiceClient\public\sample"
        private static int Main(string[] args)
        {
            try
            {
                if (args.Any(i => i.Equals("/debug", StringComparison.OrdinalIgnoreCase)))
                {
                    Debugger.Break();
                }

                if (args.Any(i => i.Equals("/?", StringComparison.OrdinalIgnoreCase)))
                {
                    return PrintUsage();
                }

                bool quiet = args.Any(i => i.StartsWith("/q", StringComparison.OrdinalIgnoreCase));

                string repositoryPath = args.SingleOrDefault(i => i[0] != '/');

                if (String.IsNullOrWhiteSpace(repositoryPath))
                {
                    return PrintUsage("You must specify a repository path");
                }

                List<string> exclusions = args.GetCommandLineArgumentValues("/e", "exclude").ToList();

                Console.WriteLine($" EnlistmentRoot: '{repositoryPath}'");
                Console.WriteLine($" Exclusion: '{String.Join($"{Environment.NewLine}  Exclusion: '", exclusions)}'");
                Console.WriteLine();

                if (!quiet)
                {
                    Console.Write("Ensure there are no files checked out in git before continuing!  Continue? (Y/N) ");
                    if (!Console.ReadLine().StartsWith("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }
                }

                using (ProjectConverter projectConverter = new ProjectConverter())
                {
                    Console.WriteLine("Converting...");

                    projectConverter.ConvertRepository(repositoryPath, exclusions);

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
            Console.WriteLine("    Repository    Full path to the repository root to convert");
            Console.WriteLine();
            Console.WriteLine("    /quiet        Do not prompt before converting the tree");
            Console.WriteLine();
            Console.WriteLine("    /exclude      One or more full paths to any directories to exclude");
            Console.WriteLine();
            Console.WriteLine("                  Example:");
            Console.WriteLine();
            Console.WriteLine("                  /exclude:\"D:\\RepoA\\src\\ProjectX\"");

            Console.WriteLine();
            Console.WriteLine("    /debug        Launch the debugger before executing");

            return String.IsNullOrWhiteSpace(errorMessage) ? 0 : 1;
        }
    }
}
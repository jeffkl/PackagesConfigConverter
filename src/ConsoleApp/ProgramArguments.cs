
using System;
using System.Collections.Generic;
using CommandLine;

namespace PackagesConfigProjectConverter
{
    // [CommandLineArguments(Program = "PackagesConfigConverter", Title = "PackagesConfigConverter", HelpText = "Converts a repository from packages.config to PackageReference")]
    public class ProgramArguments
    {
        [Option('d', HelpText = "Launch the debugger before running the program")]
        public bool Debug { get; set; }

        [Option('r', HelpText = "Full path to the repository root to convert", Required = true)]
        public string RepoRoot { get; set; }

        [Option('y', HelpText = "Suppresses prompting to confirm you want to convert the repository")]
        public bool Yes { get; set; }

        [Option('e', HelpText = "Regex for project files to exclude", MetaValue = "regex")]
        public string Exclude { get; set; }

        [Option('i', HelpText = "Regex for project files to include", MetaValue = "regex")]
        public string Include { get; set; }

        [Option('l', HelpText = "Log file to write to", MetaValue = "log")]
        public string LogFile { get; set; }

        [Option('q', HelpText = "Verbose output")]
        public bool Quiete { get; set; }

        [Option('v', HelpText = "Verbose output")]
        public bool Verbose { get; set; }
    }
}

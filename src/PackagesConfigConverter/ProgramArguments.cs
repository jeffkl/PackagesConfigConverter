// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using CommandLine;

namespace PackagesConfigConverter
{
    public class ProgramArguments
    {
        [Option('d', HelpText = "Launch the debugger before running the program")]
        public bool Debug { get; set; }

        [Option('e', HelpText = "Regex for project files to exclude", MetaValue = "regex")]
        public string Exclude { get; set; }

        [Option('i', HelpText = "Regex for project files to include", MetaValue = "regex")]
        public string Include { get; set; }

        [Option('g', HelpText = "Whether to use the MSBuild graph to discover project files")]
        public bool Graph { get; set; }

        [Option('l', HelpText = "Log file to write to", MetaValue = "log")]
        public string LogFile { get; set; }

        [Option('r', HelpText = "Full path to the repository root to convert", Required = true)]
        public string RepoRoot { get; set; }

        [Option('t', HelpText = "Trim packages to top-level dependencies")]
        public bool Trim { get; set; }

        [Option('v', HelpText = "Verbose output")]
        public bool Verbose { get; set; }

        [Option('y', HelpText = "Suppresses prompting to confirm you want to convert the repository")]
        public bool Yes { get; set; }

        [Option('f', HelpText = "The default target framework to assume if not defined in the project")]
        public string DefaultTargetFramework { get; set; }
    }
}
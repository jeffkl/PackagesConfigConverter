using CmdLine;

namespace ConsoleApp
{
    [CommandLineArguments(Program = "PackagesConfigConverter", Title = "PackagesConfigConverter", Description = "Converts a repository from packages.config to PackageReference")]
    public class ProgramArguments
    {
        [CommandLineParameter(Command = "Debug", Description = "Launch the debugger before running the program")]
        public bool Debug { get; set; }

        [CommandLineParameter(Command = "RepoRoot", Description = "Full path to the repository root to convert")]
        public string RepoRoot { get; set; }

        [CommandLineParameter(Command = "q", Description = "Do not prompt before converting the tree")]
        public bool Quiet { get; set; }

        [CommandLineParameter(Command = "e", Description = "Regex for project files to exclude")]
        public string Exclude { get; set; }

        [CommandLineParameter(Command = "i", Description = "Regex for project files to include")]
        public string Include { get; set; }

        [CommandLineParameter(Command = "UsePackagesProps", Description = "Update packages.props in the root of the repo", Default = true)]
        public bool UsePackagesProps { get; set; }
    }
}

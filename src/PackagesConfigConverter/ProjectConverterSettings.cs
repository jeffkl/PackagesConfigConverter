// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Frameworks;

namespace PackagesConfigConverter
{
    public sealed class ProjectConverterSettings
    {
        public Regex Exclude { get; set; }

        public Regex Include { get; set; }

        public bool Graph { get; set; }

        public ILogger Log { get; set; }

        public string NuGetConfigPath { get; set; } = "(Default)";

        public string RepositoryRoot { get; set; }

        public bool TrimPackages { get; set; }

        public NuGetFramework DefaultTargetFramework { get; set; } = FrameworkConstants.CommonFrameworks.Net45;
    }
}
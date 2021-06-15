// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using log4net;
using System.Text.RegularExpressions;

namespace PackagesConfigConverter
{
    public sealed class ProjectConverterSettings
    {
        public Regex Exclude { get; set; }

        public Regex Include { get; set; }

        public ILog Log { get; set; }

        public string NuGetConfigPath { get; set; } = "(Default)";

        public string RepositoryRoot { get; set; }

        public bool TrimPackages { get; set; }
    }
}
// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Linq;
using CommandLine;
using Microsoft.Build.Construction;
using NuGet.Versioning;

namespace PackagesConfigConverter
{
    internal sealed class PackageUsage
    {
        public PackageUsage(PackageInfo packageInfo, bool isDevelopmentDependency)
        {
            PackageInfo = packageInfo;
            IsDevelopmentDependency = isDevelopmentDependency;
        }

        public PackageInfo PackageInfo { get; }

        public bool IsDevelopmentDependency { get; }

        public IEnumerable<ProjectElement> AllElements => AnalyzerItems.Cast<ProjectElement>().Concat(AssemblyReferences).Concat(Imports);

        public List<ProjectItemElement> AnalyzerItems { get; } = new();

        public List<ProjectItemElement> AssemblyReferences { get; } = new();

        public List<ProjectImportElement> Imports { get; } = new();

        public bool GeneratePathProperty { get; set; }

        public bool IsMissingTransitiveDependency { get; set; }

        public string PackageId => PackageInfo.Identity.Id;

        public NuGetVersion PackageVersion => PackageInfo.Identity.Version;
    }
}
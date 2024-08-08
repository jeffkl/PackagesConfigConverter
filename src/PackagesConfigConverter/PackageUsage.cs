﻿// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using CommandLine;
using Microsoft.Build.Construction;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System.Collections.Generic;
using System.Linq;

namespace PackagesConfigConverter
{
    internal sealed class PackageUsage
    {
        public PackageUsage(PackageIdentity identity, PackageInfo packageInfo, bool isDevelopmentDependency)
        {
            PackageInfo = packageInfo;
            IsDevelopmentDependency = isDevelopmentDependency;
        }

        public PackageInfo PackageInfo { get; }

        public bool IsDevelopmentDependency { get; }

        public IEnumerable<ProjectElement> AllElements => AnalyzerItems.Cast<ProjectElement>().Concat(AssemblyReferences).Concat(Imports);

        public List<ProjectItemElement> AnalyzerItems { get; } = new ();

        public List<ProjectItemElement> AssemblyReferences { get; } = new ();

        public List<ProjectImportElement> Imports { get; } = new ();

        public bool GeneratePathProperty { get; set; }

        public bool IsMissingTransitiveDependency { get; set; }

        public string PackageId => PackageInfo.Identity.Id;

        public NuGetVersion PackageVersion => PackageInfo.Identity.Version;
    }
}

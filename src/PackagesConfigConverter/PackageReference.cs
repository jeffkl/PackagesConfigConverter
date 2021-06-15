// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using Microsoft.Build.Construction;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PackagesConfigConverter
{
    internal class PackageReference : NuGet.Packaging.PackageReference
    {
        private IReadOnlyCollection<string> _installedPackageFolderNames;

        public PackageReference(PackageIdentity identity, NuGetFramework targetFramework, bool userInstalled, bool developmentDependency, bool requireReinstallation, VersionRange allowedVersions, PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
            : base(identity, targetFramework, userInstalled, developmentDependency, requireReinstallation, allowedVersions)
        {
            InstalledPackageFilePath = PackagePathHelper.GetInstalledPackageFilePath(PackageIdentity, packagePathResolver ?? throw new ArgumentNullException(nameof(packagePathResolver)));

            RepositoryInstalledPath = Path.GetDirectoryName(InstalledPackageFilePath);

            GlobalInstalledPath = Path.GetFullPath(versionFolderPathResolver.GetInstallPath(identity.Id, identity.Version));
        }

        public IEnumerable<ProjectElement> AllElements => AnalyzerItems.Cast<ProjectElement>().Concat(AssemblyReferences).Concat(Imports);

        public List<ProjectItemElement> AnalyzerItems { get; } = new ();

        public List<ProjectItemElement> AssemblyReferences { get; } = new ();

        public bool GeneratePathProperty { get; set; }

        public string GlobalInstalledPath { get; }

        public List<ProjectImportElement> Imports { get; } = new ();

        public string InstalledPackageFilePath { get; }

        public IReadOnlyCollection<string> InstalledPackageFolderNames => _installedPackageFolderNames ??= GetPackageFolderNames();

        public bool IsMissingTransitiveDependency { get; set; }

        public string PackageId => PackageIdentity.Id;

        public NuGetVersion PackageVersion => PackageIdentity.Version;

        public string RepositoryInstalledPath { get; }

        public bool HasFolder(string name)
        {
            return InstalledPackageFolderNames.Contains(name);
        }

        private IReadOnlyCollection<string> GetPackageFolderNames()
        {
            if (RepositoryInstalledPath == null)
            {
                return Array.Empty<string>();
            }

            return new HashSet<string>(
                Directory.EnumerateDirectories(RepositoryInstalledPath)
                    .Select(i => i.Substring(i.LastIndexOf(Path.DirectorySeparatorChar) + 1)),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
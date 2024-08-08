// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.IO;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace PackagesConfigConverter
{
    internal sealed class PackageInfo
    {
        public PackageInfo(PackageIdentity identity, PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
        {
            Identity = identity;

            InstalledPackageFilePath = PackagePathHelper.GetInstalledPackageFilePath(Identity, packagePathResolver ?? throw new ArgumentNullException(nameof(packagePathResolver)));

            RepositoryInstalledPath = Path.GetDirectoryName(InstalledPackageFilePath);

            GlobalInstalledPath = Path.GetFullPath(versionFolderPathResolver.GetInstallPath(identity.Id, identity.Version));

            HasAnalyzers = Directory.Exists(Path.Combine(RepositoryInstalledPath, "analyzers"));
        }

        public string GlobalInstalledPath { get; }

        public string InstalledPackageFilePath { get; }

        public PackageIdentity Identity { get; }

        public string RepositoryInstalledPath { get; }

        public bool HasAnalyzers { get; }
    }
}
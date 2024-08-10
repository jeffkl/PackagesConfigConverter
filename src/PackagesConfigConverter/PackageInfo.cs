// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.IO;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace PackagesConfigConverter
{
    internal sealed class PackageInfo : IEquatable<PackageInfo>
    {
        public PackageInfo(PackageIdentity identity, PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
        {
            Identity = identity;
            RepositoryInstalledPath = packagePathResolver.GetInstallPath(Identity);
            GlobalInstalledPath = Path.GetFullPath(versionFolderPathResolver.GetInstallPath(identity.Id, identity.Version));
            HasAnalyzers = Directory.Exists(Path.Combine(GlobalInstalledPath, "analyzers"));
        }

        public string GlobalInstalledPath { get; }

        public PackageIdentity Identity { get; }

        public string RepositoryInstalledPath { get; }

        public bool HasAnalyzers { get; }

        public bool Equals(PackageInfo other) => Identity.Equals(other.Identity);
    }
}
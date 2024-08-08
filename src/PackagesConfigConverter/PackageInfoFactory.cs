// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System.Collections.Concurrent;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace PackagesConfigConverter
{
    internal sealed class PackageInfoFactory
    {
        private readonly PackagePathResolver _packagePathResolver;
        private readonly VersionFolderPathResolver _versionFolderPathResolver;
        private readonly ConcurrentDictionary<PackageIdentity, PackageInfo> _packages = new();

        public PackageInfoFactory(PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
        {
            _packagePathResolver = packagePathResolver;
            _versionFolderPathResolver = versionFolderPathResolver;
        }

        public PackageInfo GetPackageInfo(PackageIdentity identity) => _packages.GetOrAdd(identity, id => new PackageInfo(id, _packagePathResolver, _versionFolderPathResolver));
    }
}
// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace PackagesConfigConverter
{
    internal sealed class PackageInfo
    {
        private IReadOnlyCollection<string> _installedPackageFolderNames;

        public PackageInfo(PackageIdentity identity, PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
        {
            Identity = identity;

            InstalledPackageFilePath = PackagePathHelper.GetInstalledPackageFilePath(Identity, packagePathResolver ?? throw new ArgumentNullException(nameof(packagePathResolver)));

            RepositoryInstalledPath = Path.GetDirectoryName(InstalledPackageFilePath);

            GlobalInstalledPath = Path.GetFullPath(versionFolderPathResolver.GetInstallPath(identity.Id, identity.Version));
        }

        public string GlobalInstalledPath { get; }

        public string InstalledPackageFilePath { get; }

        public IReadOnlyCollection<string> InstalledPackageFolderNames => _installedPackageFolderNames ??= GetPackageFolderNames();

        public PackageIdentity Identity { get; }

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

            var folderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.EnumerateDirectories(RepositoryInstalledPath))
            {
                if (HasAnyRealFiles(dir))
                {
                    string dirName = dir.Substring(dir.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                    folderNames.Add(dirName);
                }
            }

            return folderNames;

            static bool HasAnyRealFiles(string dir)
            {
                // Ignore the special "_._" file.
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (!Path.GetFileName(file).Equals("_._"))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
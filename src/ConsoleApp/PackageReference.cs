using Microsoft.Build.Construction;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PackagesConfigProjectConverter
{
    internal class PackageReference : NuGet.Packaging.PackageReference
    {
        private IReadOnlyCollection<string> _installedPackageFolderNames = null;

        public PackageReference(PackageIdentity identity, NuGetFramework targetFramework, bool userInstalled, bool developmentDependency, bool requireReinstallation, VersionRange allowedVersions, PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
            : base(identity, targetFramework, userInstalled, developmentDependency, requireReinstallation, allowedVersions)
        {
            InstalledPackageFilePath = PackagePathHelper.GetInstalledPackageFilePath(PackageIdentity, packagePathResolver ?? throw new ArgumentNullException(nameof(packagePathResolver)));

            RepositoryInstalledPath = Path.GetDirectoryName(InstalledPackageFilePath);

            GlobalInstalledPath = Path.GetFullPath(versionFolderPathResolver.GetInstallPath(identity.Id, identity.Version));
        }

        public IEnumerable<ProjectElement> AllElements => AnalyzerItems.Cast<ProjectElement>().Concat(AssemblyReferences).Concat(Imports);
        public List<ProjectItemElement> AnalyzerItems { get; } = new List<ProjectItemElement>();
        public List<ProjectItemElement> AssemblyReferences { get; } = new List<ProjectItemElement>();
        public string GlobalInstalledPath { get; }
        public List<ProjectImportElement> Imports { get; } = new List<ProjectImportElement>();

        public string InstalledPackageFilePath { get; }

        public IReadOnlyCollection<string> InstalledPackageFolderNames => _installedPackageFolderNames ?? (_installedPackageFolderNames = GetPackageFolderNames());

        public string PackageId => PackageIdentity.Id;

        public NuGetVersion PackageVersion => PackageIdentity.Version;

        public string RepositoryInstalledPath { get; }

        public bool IsMissingTransitiveDependency { get; set; }

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
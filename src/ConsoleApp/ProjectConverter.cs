using log4net;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Configuration;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using NuGet.Commands;
using NuGet.Common;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

// ReSharper disable CollectionNeverUpdated.Local
namespace PackagesConfigProjectConverter
{
    internal sealed class ProjectConverter : IProjectConverter
    {
        private const string CommonPackagesGroupLabel = "Package versions used by this repository";
        private static readonly HashSet<string> ItemsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "packages.config" };
        private static readonly HashSet<string> PropertiesToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NuGetPackageImportStamp" };
        private static readonly HashSet<string> TargetsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EnsureNuGetPackageBuildImports" };
        private readonly ProjectConverterSettings _converterSettings;
        private readonly string _globalPackagesFolder;
        private readonly ISettings _nugetSettings;
        private readonly ProjectCollection _projectCollection = new ProjectCollection();
        private readonly string _repositoryPath;

        public ProjectConverter(ProjectConverterSettings converterSettings)
            : this(converterSettings, GetNuGetSettings(converterSettings))
        {
            _converterSettings = converterSettings ?? throw new ArgumentNullException(nameof(converterSettings));
        }

        public ProjectConverter(ProjectConverterSettings converterSettings, ISettings nugetSettings)
        {
            _converterSettings = converterSettings ?? throw new ArgumentNullException(nameof(converterSettings));

            _nugetSettings = nugetSettings ?? throw new ArgumentNullException(nameof(nugetSettings));

            _repositoryPath = Path.GetFullPath(SettingsUtility.GetRepositoryPath(_nugetSettings)).Trim(Path.DirectorySeparatorChar);

            _globalPackagesFolder = Path.GetFullPath(SettingsUtility.GetGlobalPackagesFolder(_nugetSettings)).Trim(Path.DirectorySeparatorChar);

            PackagePathResolver = new PackagePathResolver(_repositoryPath);

            VersionFolderPathResolver = new VersionFolderPathResolver(_globalPackagesFolder);
        }

        public ILog Log => _converterSettings.Log;

        public PackagePathResolver PackagePathResolver { get; internal set; }

        public VersionFolderPathResolver VersionFolderPathResolver { get; internal set; }

        public void ConvertRepository(CancellationToken cancellationToken)
        {
            bool success = true;

            Log.Info($"Converting repository \"{_converterSettings.RepositoryRoot}\"...");

            Log.Info($"  NuGet configuration file : \"{Path.Combine(_nugetSettings.Root, _nugetSettings.FileName)}\"");

            foreach (string file in Directory.EnumerateFiles(_converterSettings.RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
                .TakeWhile(_ => !cancellationToken.IsCancellationRequested)
                .Where(f => _converterSettings.Exclude == null || !_converterSettings.Exclude.IsMatch(f))
                .Where(f => _converterSettings.Include == null || _converterSettings.Include.IsMatch(f)))
            {
                if (_converterSettings.Exclude != null && _converterSettings.Exclude.IsMatch(file))
                {
                    Log.Debug($"  Excluding file \"{file}\"");
                    continue;
                }

                if (_converterSettings.Include != null && !_converterSettings.Include.IsMatch(file))
                {
                    Log.Debug($"  Not including file \"{file}\"");
                    continue;
                }

                string packagesConfigPath = Path.Combine(Path.GetDirectoryName(file), "packages.config");

                if (!File.Exists(packagesConfigPath))
                {
                    Log.Debug($"  Skipping project \"{file}\" because it does not have a packages.config");
                    continue;
                }

                if (!ConvertProject(file, packagesConfigPath))
                {
                    success = false;
                }
            }

            if (success)
            {
                Log.Info("Successfully converted repository");
            }
        }

        public void Dispose()
        {
            _projectCollection?.Dispose();
        }

        private static ISettings GetNuGetSettings(ProjectConverterSettings converterSettings)
        {
            string nugetConfigPath = Path.Combine(converterSettings.RepositoryRoot, "NuGet.config");

            if (File.Exists(nugetConfigPath))
            {
                return Settings.LoadSpecificSettings(converterSettings.RepositoryRoot, Settings.DefaultSettingsFileName);
            }

            return Settings.LoadDefaultSettings(converterSettings.RepositoryRoot, Settings.DefaultSettingsFileName, new XPlatMachineWideSetting());
        }

        private ProjectItemElement AddPackageReference(ProjectItemGroupElement itemGroupElement, PackageReference package)
        {
            LibraryIncludeFlags excludeAssets = LibraryIncludeFlags.None;

            LibraryIncludeFlags privateAssets = LibraryIncludeFlags.None;

            if (package.HasFolder("build") && package.Imports.Count == 0)
            {
                excludeAssets |= LibraryIncludeFlags.Build;
            }

            if (package.HasFolder("lib") && package.AssemblyReferences.Count == 0)
            {
                excludeAssets |= LibraryIncludeFlags.Compile;
                excludeAssets |= LibraryIncludeFlags.Runtime;
            }

            if (package.HasFolder("analyzers") && package.AnalyzerItems.Count == 0)
            {
                excludeAssets |= LibraryIncludeFlags.Analyzers;
            }

            if (package.IsDevelopmentDependency)
            {
                privateAssets |= LibraryIncludeFlags.All;
            }

            ProjectItemElement itemElement = itemGroupElement.AppendItem("PackageReference", package.PackageIdentity.Id);

            itemElement.AddMetadataAsAttribute("Version", package.PackageVersion.ToNormalizedString());

            if (excludeAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("ExcludeAssets", excludeAssets.ToString());
            }

            if (privateAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("PrivateAssets", privateAssets.ToString());
            }

            return itemElement;
        }

        private void Test(List<PackageReference> packages, string projectPath)
        {
            List<NuGetFramework> targetFrameworks = new List<NuGetFramework>
            {
                FrameworkConstants.CommonFrameworks.Net45
            };

            using (var sourceCacheContext = new SourceCacheContext
            {
                IgnoreFailedSources = true,
            })
            {
                // The package spec details what packages to restore
                var packageSpec = new PackageSpec(targetFrameworks.Select(i => new TargetFrameworkInformation
                {
                    FrameworkName = i
                }).ToList())
                {
                    //Dependencies = new List<LibraryDependency>
                    //{
                    //    new LibraryDependency
                    //    {
                    //        LibraryRange = new LibraryRange(id, new VersionRange(NuGetVersion.Parse(version)), LibraryDependencyTarget.Package),
                    //        SuppressParent = LibraryIncludeFlags.All,
                    //        AutoReferenced = true,
                    //        IncludeType = LibraryIncludeFlags.None,
                    //        Type = LibraryDependencyType.Build
                    //    }
                    //},
                    Dependencies = packages.Select(i => new LibraryDependency
                    {
                        LibraryRange = new LibraryRange(i.PackageId, new VersionRange(i.PackageVersion), LibraryDependencyTarget.Package),
                        //SuppressParent = LibraryIncludeFlags.All,
                        //AutoReferenced = true,
                        //IncludeType = LibraryIncludeFlags.None,
                        //Type = LibraryDependencyType.
                    }).ToList(),
                    RestoreMetadata = new ProjectRestoreMetadata
                    {
                        ProjectPath = projectPath,
                        ProjectName = Path.GetFileNameWithoutExtension(projectPath),
                        ProjectStyle = ProjectStyle.PackageReference,
                        ProjectUniqueName = projectPath,
                        OutputPath = Path.GetTempPath(),
                        OriginalTargetFrameworks = targetFrameworks.Select(i => i.ToString()).ToList(),
                        ConfigFilePaths = SettingsUtility.GetConfigFilePaths(_nugetSettings).ToList(),
                        PackagesPath = SettingsUtility.GetGlobalPackagesFolder(_nugetSettings),
                        Sources = SettingsUtility.GetEnabledSources(_nugetSettings).ToList(),
                        FallbackFolders = SettingsUtility.GetFallbackPackageFolders(_nugetSettings).ToList()
                    },
                    FilePath = projectPath,
                    Name = Path.GetFileNameWithoutExtension(projectPath),
                };

                var dependencyGraphSpec = new DependencyGraphSpec();

                dependencyGraphSpec.AddProject(packageSpec);

                dependencyGraphSpec.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);

                IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), dependencyGraphSpec);

                var restoreArgs = new RestoreArgs
                {
                    AllowNoOp = true,
                    CacheContext = sourceCacheContext,
                    CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(_nugetSettings)),
                    Log = NullLogger.Instance,
                };

                // Create requests from the arguments
                var requests = requestProvider.CreateRequests(restoreArgs).Result;

                // Restore the package without generating extra files
                RestoreResultPair foo = RestoreRunner.RunWithoutCommit(requests, restoreArgs).Result.FirstOrDefault();

                foreach (LibraryIdentity library in foo.Result.RestoreGraphs.FirstOrDefault().Flattened.Where(i => i.Key.Type == LibraryType.Package).Select(i => i.Key))
                {
                    var mat = packages.FirstOrDefault(i => i.PackageId.Equals(library.Name, StringComparison.OrdinalIgnoreCase));

                    if (mat == null)
                    {

                    }
                }
            }
        }

        private bool ConvertProject(string projectPath, string packagesConfigPath)
        {
            try
            {
                Log.Info($"  Converting project \"{projectPath}\"");

                PackagesConfigReader packagesConfigReader = new PackagesConfigReader(XDocument.Load(packagesConfigPath));

                List<PackageReference> packages = packagesConfigReader.GetPackages(allowDuplicatePackageIds: true).Select(i => new PackageReference(i.PackageIdentity, i.TargetFramework, i.IsUserInstalled, i.IsDevelopmentDependency, i.RequireReinstallation, i.AllowedVersions, PackagePathResolver, VersionFolderPathResolver)).ToList();

                Test(packages, projectPath);

                //foreach (PackageReference package in packages)
                //{
                //    var bar = foo.GetPackage(package.PackageIdentity, NullLogger.Instance, CancellationToken.None);

                //    var group = NuGetFrameworkUtility.GetNearest<PackageDependencyGroup>(bar.Nuspec.GetDependencyGroups(), NuGetFramework.Parse("net45"));

                //    var result = new SourcePackageDependencyInfo(
                //        bar.Identity,
                //        group.Packages,
                //        listed: true,
                //        source: null,
                //        downloadUri: UriUtility.CreateSourceUri(bar.Path, UriKind.Absolute),
                //        packageHash: null);
                //}
                

                ProjectRootElement project = ProjectRootElement.Open(projectPath, _projectCollection, preserveFormatting: true);

                ProjectItemGroupElement itemGroupElement = null;

                foreach (ProjectElement element in project.AllChildren)
                {
                    ProcessElement(element, packages);
                }

                if (itemGroupElement == null)
                {
                    itemGroupElement = project.AddItemGroup();
                }

                Log.Info("    Current package references:");

                foreach (PackageReference package in packages)
                {
                    Log.Info($"      {package.PackageIdentity}");
                }

                Log.Info("    Converted package references:");

                foreach (PackageReference package in packages)
                {
                    ProjectItemElement packageReferenceItemElement = AddPackageReference(itemGroupElement, package);

                    Log.Info($"      {packageReferenceItemElement.ToXmlString()}");
                }

                foreach (ProjectElement element in packages.SelectMany(i => i.AllElements))
                {
                    Log.Debug($"    {element.Location}: Removing element {element.ToXmlString()}");
                    element.Remove();
                }

                if (project.HasUnsavedChanges)
                {
                    Log.Debug($"    Saving project \"{project.FullPath}\"");
                    project.Save();
                }

                Log.Debug($"    Deleting file \"{packagesConfigPath}\"");

                File.Delete(packagesConfigPath);

                Log.Info($"  Successfully converted \"{project.FullPath}\"");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to convert '{projectPath}' : {e}");

                return false;
            }

            return true;
        }

        private Match GetElementMatch(ElementPath elementPath, PackageReference package)
        {
            Match match = null;

            if (elementPath.Element != null)
            {
                switch (elementPath.Element)
                {
                    case ProjectItemElement itemElement:

                        if (itemElement.ItemType.Equals("Analyzer", StringComparison.OrdinalIgnoreCase))
                        {
                            match = RegularExpressions.Analyzers[package.PackageIdentity].Match(elementPath.FullPath);

                            if (match.Success)
                            {
                                package.AnalyzerItems.Add(itemElement);
                            }
                        }
                        else if (itemElement.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(elementPath.FullPath))
                            {
                                match = RegularExpressions.AssemblyReferences[package.PackageIdentity].Match(elementPath.FullPath);

                                if (match.Success)
                                {
                                    package.AssemblyReferences.Add(itemElement);
                                }
                            }
                        }

                        break;

                    case ProjectImportElement importElement:

                        match = RegularExpressions.Imports[package.PackageIdentity].Match(elementPath.FullPath);

                        if (match.Success)
                        {
                            package.Imports.Add(importElement);
                        }
                        break;
                }
            }

            return match;
        }

        private bool IsPathRootedInRepositoryPath(string path)
        {
            return path != null && path.StartsWith(_repositoryPath, StringComparison.OrdinalIgnoreCase);
        }

        private void ProcessElement(ProjectElement element, List<PackageReference> packages)
        {
            switch (element)
            {
                case ProjectPropertyElement propertyElement:
                    if (PropertiesToRemove.Contains(propertyElement.Name))
                    {
                        Log.Debug($"    {element.Location}: Removing property \"{element.ElementName}\"");
                        propertyElement.Remove();
                        return;
                    }
                    break;

                case ProjectItemElement itemElement:
                    if (ItemsToRemove.Contains(itemElement.ItemType) || ItemsToRemove.Contains(itemElement.Include))
                    {
                        Log.Debug($"    {element.Location}: Removing item \"{itemElement.ItemType}\"");
                        itemElement.Remove();
                        return;
                    }

                    break;

                case ProjectTargetElement targetElement:
                    if (TargetsToRemove.Contains(targetElement.Name))
                    {
                        Log.Debug($"    {element.Location}: Removing target \"{targetElement.Name}\"");
                        targetElement.Remove();
                        return;
                    }
                    break;
            }

            ElementPath elementPath = new ElementPath(element);

            bool elementPathIsValid = false;

            foreach (PackageReference package in packages)
            {
                Match match = GetElementMatch(elementPath, package);

                if (match != null && match.Success && match.Groups["version"] != null && match.Groups["version"].Success && NuGetVersion.TryParse(match.Groups["version"].Value, out NuGetVersion version))
                {
                    elementPathIsValid = true;

                    if (!version.Equals(package.PackageIdentity.Version))
                    {
                        Log.Warn($"  {element.Location}: The package version \"{version}\" specified in the \"{element.ElementName}\" element does not match the package version \"{package.PackageVersion}\".  After conversion, the project will reference ONLY version \"{package.PackageVersion}\"");
                    }
                }
            }

            if (!elementPathIsValid && IsPathRootedInRepositoryPath(elementPath.FullPath))
            {
                PackageReference package = packages.FirstOrDefault(i => elementPath.FullPath.StartsWith(i.RepositoryInstalledPath));

                if (package == null)
                {
                    // TODO: Using a package that isn't referenced
                }
                else
                {
                    string path = Path.Combine(package.GlobalInstalledPath, elementPath.FullPath.Substring(package.RepositoryInstalledPath.Length + 1));

                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        // TODO: Using a path that doesn't exist?
                    }
                    else
                    {
                        path = $"$(NuGetPackageRoot){path.Substring(_globalPackagesFolder.Length)}";

                        elementPath.Set(path);
                    }
                }
            }
        }

        private void ReplaceReferences(ProjectRootElement project, List<PackageIdentity> packages, ProjectRootElement packagesProps)
        {
            HashSet<PackageIdentity> allPackages = new HashSet<PackageIdentity>(packages);

            Dictionary<ProjectItemElement, PackageIdentity> itemsToReplace = new Dictionary<ProjectItemElement, PackageIdentity>();

            foreach (ProjectItemElement itemElement in project.Items.Where(i => i.ItemType.Equals("Reference")))
            {
                foreach (PackageIdentity packageIdentity in packages)
                {
                    Regex regex = RegularExpressions.AssemblyReferences[packageIdentity];

                    ProjectMetadataElement metadatum = itemElement.Metadata.FirstOrDefault(i => i.Name.Equals("HintPath"));

                    if (metadatum != null && regex.IsMatch(metadatum.Value))
                    {
                        itemsToReplace.Add(itemElement, packageIdentity);

                        allPackages.Remove(packageIdentity);
                    }
                }
            }

            List<PackageIdentity> packagesAdded = new List<PackageIdentity>();

            ProjectItemElement firstPackageRef = project.Items.FirstOrDefault(i => i.ItemType.Equals("PackageReference"));
            ProjectItemGroupElement packageRefsGroup;
            if (firstPackageRef == null)
            {
                packageRefsGroup = project.AddItemGroup();
            }
            else
            {
                packageRefsGroup = (ProjectItemGroupElement)firstPackageRef.Parent;
            }

            foreach (KeyValuePair<ProjectItemElement, PackageIdentity> pair in itemsToReplace)
            {
                if (!packagesAdded.Contains(pair.Value))
                {
                    ProjectItemElement item = project.CreateItemElement("PackageReference", pair.Value.Id);

                    packageRefsGroup.AddItem("PackageReference", pair.Value.Id);

                    SetPackageVersion(item, pair.Value.Version, packagesProps);

                    packagesAdded.Add(pair.Value);
                }

                pair.Key.Parent.RemoveChild(pair.Key);
            }

            foreach (PackageIdentity package in allPackages)
            {
                ProjectItemElement item = packageRefsGroup.AddItem("PackageReference", package.Id);
                SetPackageVersion(item, package.Version, packagesProps);
            }
        }

        private void SetPackageVersion(ProjectItemElement packageRef, NuGetVersion version, ProjectRootElement packagesProps)
        {
            if (packagesProps == null)
            {
                packageRef.AddMetadata("Version", version.ToString(), expressAsAttribute: true);
                return;
            }

            ProjectItemElement commonPackageVersion = packagesProps.Items
                .FirstOrDefault(i => i.ItemType.Equals("PackageVersion", StringComparison.OrdinalIgnoreCase) &&
                                        i.Include.Equals(packageRef.Include, StringComparison.OrdinalIgnoreCase));

            string versionStr = $"[{version.ToNormalizedString()}]";
            if (commonPackageVersion != null)
            {
                ProjectMetadataElement commonVersion = commonPackageVersion.Metadata.FirstOrDefault(i => i.Name.Equals("Version"));
                if (commonVersion != null)
                {
                    if (!versionStr.Equals(commonVersion.Value.ToString()))
                    {
                        throw new InvalidDataException($"For package {packageRef.Include} version {version} conflicts with {commonVersion.Value} [project {packageRef.ContainingProject.FullPath}]");
                    }
                    return;
                }
            }

            ProjectItemGroupElement commonPackagesGroup = packagesProps.ItemGroups.SingleOrDefault(g => g.Label.Equals(CommonPackagesGroupLabel, StringComparison.OrdinalIgnoreCase));
            if (commonPackagesGroup == null)
            {
                throw new InvalidOperationException($"{packagesProps.FullPath} is expected to contain ItemGroup with label '{CommonPackagesGroupLabel}'");
            }

            ProjectItemElement commonVersionItem = commonPackagesGroup.AddItem("PackageVersion", packageRef.Include.ToString());
            commonVersionItem.AddMetadata("Version", versionStr.ToString(), expressAsAttribute: true);
        }
    }
}
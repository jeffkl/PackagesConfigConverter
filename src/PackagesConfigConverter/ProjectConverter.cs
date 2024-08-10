// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// ReSharper disable CollectionNeverUpdated.Local
namespace PackagesConfigConverter
{
    internal sealed class ProjectConverter : IProjectConverter
    {
        private static readonly HashSet<string> ItemsToRemove = new(StringComparer.OrdinalIgnoreCase) { "packages.config" };
        private static readonly HashSet<string> PropertiesToRemove = new(StringComparer.OrdinalIgnoreCase) { "NuGetPackageImportStamp" };
        private static readonly HashSet<string> TargetsToRemove = new(StringComparer.OrdinalIgnoreCase) { "EnsureNuGetPackageBuildImports" };
        private readonly ProjectConverterSettings _converterSettings;
        private readonly string _globalPackagesFolder;
        private readonly ISettings _nugetSettings;
        private readonly ProjectCollection _projectCollection = new();
        private readonly string _repositoryPath;
        private readonly PackageInfoFactory _packageInfoFactory;
        private readonly RestoreCommandProvidersCache _restoreCommandProvidersCache;
        private readonly RestoreArgs _restoreArgs;

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

            var packagePathResolver = new PackagePathResolver(_repositoryPath);
            var versionFolderPathResolver = new VersionFolderPathResolver(_globalPackagesFolder);
            _packageInfoFactory = new PackageInfoFactory(packagePathResolver, versionFolderPathResolver);

            _restoreCommandProvidersCache = new RestoreCommandProvidersCache();
            _restoreArgs = new RestoreArgs
            {
                AllowNoOp = true,
                CacheContext = new SourceCacheContext()
                {
                    IgnoreFailedSources = true,
                },
                CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(_nugetSettings)),
                GlobalPackagesFolder = _globalPackagesFolder,
                Log = NullLogger.Instance,
            };
        }

        public ILogger Log => _converterSettings.Log;

        public void ConvertRepository(CancellationToken cancellationToken)
        {
            bool success = true;

            Log.LogInformation($"Converting repository \"{_converterSettings.RepositoryRoot}\"...");

            Log.LogInformation($"  NuGet configuration file : \"{_converterSettings.NuGetConfigPath}\"");

            foreach (string file in Directory.EnumerateFiles(_converterSettings.RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
                .TakeWhile(_ => !cancellationToken.IsCancellationRequested))
            {
                if (_converterSettings.Exclude != null && _converterSettings.Exclude.IsMatch(file))
                {
                    Log.LogDebug($"  Excluding file \"{file}\"");
                    continue;
                }

                if (_converterSettings.Include != null && !_converterSettings.Include.IsMatch(file))
                {
                    Log.LogDebug($"  Not including file \"{file}\"");
                    continue;
                }

                string packagesConfigPath = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, "packages.config");

                if (!File.Exists(packagesConfigPath))
                {
                    Log.LogDebug($"  Skipping project \"{file}\" because it does not have a packages.config");
                    continue;
                }

                if (!ConvertProject(file, packagesConfigPath))
                {
                    success = false;
                }
            }

            if (success)
            {
                Log.LogInformation("Successfully converted repository");
            }
        }

        public void Dispose()
        {
            _projectCollection.Dispose();
            _restoreArgs.CacheContext.Dispose();
        }

        private static ISettings GetNuGetSettings(ProjectConverterSettings converterSettings)
        {
            string nugetConfigPath = Path.Combine(converterSettings.RepositoryRoot, "NuGet.config");

            if (File.Exists(nugetConfigPath))
            {
                converterSettings.NuGetConfigPath = nugetConfigPath;

                return Settings.LoadSpecificSettings(converterSettings.RepositoryRoot, Settings.DefaultSettingsFileName);
            }

            return Settings.LoadDefaultSettings(converterSettings.RepositoryRoot, Settings.DefaultSettingsFileName, new XPlatMachineWideSetting());
        }

        private ProjectItemElement AddPackageReference(ProjectItemGroupElement itemGroupElement, PackageUsage package, LockFileTargetLibrary lockFileTargetLibrary, bool isDevelopmentDependency)
        {
            LibraryIncludeFlags existingAssets = LibraryIncludeFlags.None;

            if (HasAnyRealFiles(lockFileTargetLibrary.Build))
            {
                existingAssets |= LibraryIncludeFlags.Build;
            }

            if (HasAnyRealFiles(lockFileTargetLibrary.CompileTimeAssemblies))
            {
                existingAssets |= LibraryIncludeFlags.Compile;
            }

            if (HasAnyRealFiles(lockFileTargetLibrary.RuntimeAssemblies))
            {
                existingAssets |= LibraryIncludeFlags.Runtime;
            }

            // LockFileTargetLibrary doesn't include analyzer information, so we have to figure it out ourselves.
            if (package.PackageInfo.HasAnalyzers)
            {
                existingAssets |= LibraryIncludeFlags.Analyzers;
            }

            LibraryIncludeFlags includeAssets;
            LibraryIncludeFlags excludeAssets;
            LibraryIncludeFlags privateAssets;

            if (package.IsMissingTransitiveDependency)
            {
                includeAssets = LibraryIncludeFlags.None;
                excludeAssets = LibraryIncludeFlags.None;
                privateAssets = LibraryIncludeFlags.All;
            }
            else
            {
                includeAssets = LibraryIncludeFlags.All;
                excludeAssets = existingAssets & ~package.UsedAssets;
                privateAssets = isDevelopmentDependency ? LibraryIncludeFlags.All : LibraryIncludeFlags.None;
            }

            // Simplify if the inclusions or exclusions exactly equal what's in the package.
            if (includeAssets == existingAssets)
            {
                includeAssets = LibraryIncludeFlags.All;
            }

            if (excludeAssets == existingAssets)
            {
                excludeAssets = LibraryIncludeFlags.All;
            }

            if (privateAssets == existingAssets)
            {
                privateAssets = LibraryIncludeFlags.All;
            }

            return AddPackageReference(itemGroupElement, package.PackageInfo.Identity, includeAssets, excludeAssets, privateAssets, package.GeneratePathProperty);

            static bool HasAnyRealFiles(IList<LockFileItem> items)
            {
                if (items.Count > 0)
                {
                    // Ignore the special "_._" file.
                    foreach (LockFileItem item in items)
                    {
                        if (!Path.GetFileName(item.Path).Equals("_._"))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        private ProjectItemElement AddPackageReference(
            ProjectItemGroupElement itemGroupElement,
            PackageIdentity packageIdentity,
            LibraryIncludeFlags includeAssets,
            LibraryIncludeFlags excludeAssets,
            LibraryIncludeFlags privateAssets,
            bool generatePathProperty)
        {
            ProjectItemElement itemElement = itemGroupElement.AppendItem("PackageReference", packageIdentity.Id);

            itemElement.AddMetadataAsAttribute("Version", packageIdentity.Version.ToNormalizedString());

            if (includeAssets != LibraryIncludeFlags.All)
            {
                itemElement.AddMetadataAsAttribute("IncludeAssets", includeAssets.ToString());
            }

            if (excludeAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("ExcludeAssets", excludeAssets.ToString());
            }

            if (privateAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("PrivateAssets", privateAssets.ToString());
            }

            if (generatePathProperty)
            {
                itemElement.AddMetadataAsAttribute("GeneratePathProperty", bool.TrueString);
            }

            return itemElement;
        }

        private bool ConvertProject(string projectPath, string packagesConfigPath)
        {
            try
            {
                Log.LogInformation($"  Converting project \"{projectPath}\"");

                PackagesConfigReader packagesConfigReader = new PackagesConfigReader(XDocument.Load(packagesConfigPath));

                HashSet<PackageIdentity> developmentDependencies = new();
                List<PackageInfo> packages = packagesConfigReader
                    .GetPackages(allowDuplicatePackageIds: true)
                    .Select(i =>
                    {
                        if (i.IsDevelopmentDependency)
                        {
                            developmentDependencies.Add(i.PackageIdentity);
                        }

                        return _packageInfoFactory.GetPackageInfo(i.PackageIdentity);
                    })
                    .ToList();

                Log.LogDebug("    Current package references:");

                foreach (PackageInfo package in packages)
                {
                    Log.LogDebug($"      {package.Identity}");
                }

                ProjectRootElement project = ProjectRootElement.Open(projectPath, _projectCollection, preserveFormatting: true);

                NuGetFramework targetFramework = GetNuGetFramework(project);

                RestoreResult restoreResult = Restore(packages, projectPath, targetFramework);
                RestoreTargetGraph restoreTargetGraph = restoreResult.RestoreGraphs.FirstOrDefault();
                LockFile lockFile = restoreResult.LockFile;

                Dictionary<PackageInfo, PackageUsage> packageUsages = new(packages.Count);
                foreach (PackageInfo package in packages)
                {
                    packageUsages.Add(package, new PackageUsage(package));
                }

                DetectMissingTransitiveDependencies(packageUsages, projectPath, restoreTargetGraph);

                ProjectItemGroupElement packageReferenceItemGroupElement = null;

                foreach (ProjectElement element in project.AllChildren)
                {
                    ProcessElement(element, packageUsages);

                    if (packageReferenceItemGroupElement == null && element is ProjectItemElement { ItemType: "Reference" })
                    {
                        // Find the first Reference item and use it to add PackageReference items to, otherwise a new ItemGroup is added
                        packageReferenceItemGroupElement = element.Parent as ProjectItemGroupElement;
                    }
                }

                packageReferenceItemGroupElement ??= project.AddItemGroup();

                if (_converterSettings.TrimPackages)
                {
                    TrimPackages(packageUsages, projectPath, restoreTargetGraph);
                }

                Log.LogDebug("    Converted package references:");

                LockFileTarget lockFileTarget = lockFile.GetTarget(targetFramework, runtimeIdentifier: null);
                foreach (KeyValuePair<PackageInfo, PackageUsage> kvp in packageUsages)
                {
                    PackageInfo package = kvp.Key;
                    PackageUsage packageUsage = kvp.Value;

                    LockFileTargetLibrary lockFileTargetLibrary = lockFileTarget.GetTargetLibrary(packageUsage.PackageId);
                    bool isDevelopmentDependency = developmentDependencies.Contains(package.Identity);

                    ProjectItemElement packageReferenceItemElement = AddPackageReference(packageReferenceItemGroupElement, packageUsage, lockFileTargetLibrary, isDevelopmentDependency);
                    Log.LogDebug($"      {packageReferenceItemElement.ToXmlString()}");
                }

                if (project.HasUnsavedChanges)
                {
                    Log.LogDebug($"    Saving project \"{project.FullPath}\"");
                    project.Save();
                }

                Log.LogDebug($"    Deleting file \"{packagesConfigPath}\"");

                File.Delete(packagesConfigPath);

                Log.LogInformation($"  Successfully converted \"{project.FullPath}\"");
            }
            catch (Exception e)
            {
                Log.LogError(e, $"Failed to convert '{projectPath}'");

                return false;
            }

            return true;
        }

        private void DetectMissingTransitiveDependencies(Dictionary<PackageInfo, PackageUsage> packages, string projectPath, RestoreTargetGraph restoreTargetGraph)
        {
            IEnumerable<GraphItem<RemoteResolveResult>> flatPackageDependencies = restoreTargetGraph.Flattened.Where(i => i.Key.Type == LibraryType.Package);
            foreach (GraphItem<RemoteResolveResult> packageDependency in flatPackageDependencies)
            {
                LibraryIdentity library = packageDependency.Key;
                PackageIdentity packageIdentity = new PackageIdentity(library.Name, library.Version);
                PackageInfo packageInfo = _packageInfoFactory.GetPackageInfo(packageIdentity);

                if (!packages.ContainsKey(packageInfo))
                {
                    Log.LogWarning($"{projectPath}: The transitive package dependency \"{library.Name} {library.Version}\" was not in the packages.config.  After converting to PackageReference, new dependencies will be pulled in transitively which could lead to restore or build errors");

                    PackageUsage missingTransitiveDependency = new PackageUsage(packageInfo);
                    missingTransitiveDependency.IsMissingTransitiveDependency = true;
                    packages.Add(packageInfo, missingTransitiveDependency);
                }
            }
        }

        private Match GetElementMatch(ElementPath elementPath, PackageUsage packageUsage)
        {
            Match match = null;

            if (elementPath.Element != null)
            {
                switch (elementPath.Element)
                {
                    case ProjectItemElement itemElement:

                        if (itemElement.ItemType.Equals("Analyzer", StringComparison.OrdinalIgnoreCase))
                        {
                            match = RegularExpressions.Analyzers[packageUsage.PackageInfo.Identity].Match(elementPath.FullPath);

                            if (match.Success)
                            {
                                packageUsage.UsedAssets |= LibraryIncludeFlags.Analyzers;
                                RemoveElement(elementPath.Element);
                            }
                        }
                        else if (itemElement.ItemType.Equals("Reference", StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(elementPath.FullPath))
                            {
                                match = RegularExpressions.AssemblyReferences[packageUsage.PackageInfo.Identity].Match(elementPath.FullPath);

                                if (match.Success)
                                {
                                    packageUsage.UsedAssets |= LibraryIncludeFlags.Compile | LibraryIncludeFlags.Runtime;
                                    RemoveElement(elementPath.Element);
                                }
                            }
                        }

                        break;

                    case ProjectImportElement:

                        match = RegularExpressions.Imports[packageUsage.PackageInfo.Identity].Match(elementPath.FullPath);

                        if (match.Success)
                        {
                            packageUsage.UsedAssets |= LibraryIncludeFlags.Build;
                            RemoveElement(elementPath.Element);
                        }

                        break;
                }
            }

            return match;

            void RemoveElement(ProjectElement element)
            {
                Log.LogDebug($"    {element.Location}: Removing element {element.ToXmlString()}");
                element.Remove();
            }
        }

        private NuGetFramework GetNuGetFramework(ProjectRootElement project)
        {
            string targetFramework = project.PropertyGroups.SelectMany(p => p.Properties).LastOrDefault(p => p.Name.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))?.Value;
            if (targetFramework != null)
            {
                return NuGetFramework.Parse(targetFramework);
            }

            string targetFrameworkVersion = project.PropertyGroups.SelectMany(p => p.Properties).LastOrDefault(p => p.Name.Equals("TargetFrameworkVersion", StringComparison.OrdinalIgnoreCase))?.Value;
            if (targetFrameworkVersion != null)
            {
                return NuGetFramework.Parse($".NETFramework,Version={targetFrameworkVersion}");
            }

            Log.LogDebug($"    Could not find target framework. Using default {_converterSettings.DefaultTargetFramework}");
            return _converterSettings.DefaultTargetFramework;
        }

        private RestoreResult Restore(List<PackageInfo> packages, string projectPath, NuGetFramework framework)
        {
            List<TargetFrameworkInformation> targetFrameworks = new List<TargetFrameworkInformation>
            {
                new TargetFrameworkInformation { FrameworkName = framework },
            };

            // The package spec details what packages to restore
            PackageSpec packageSpec = new PackageSpec(targetFrameworks)
            {
                Dependencies = packages.Select(i => new LibraryDependency
                {
                    LibraryRange = new LibraryRange(i.Identity.Id, new VersionRange(i.Identity.Version), LibraryDependencyTarget.Package),
                }).ToList(),
                RestoreMetadata = new ProjectRestoreMetadata
                {
                    ProjectPath = projectPath,
                    ProjectName = Path.GetFileNameWithoutExtension(projectPath),
                    ProjectStyle = ProjectStyle.PackageReference,
                    ProjectUniqueName = projectPath,
                    OutputPath = Path.GetTempPath(),
                    OriginalTargetFrameworks = new List<string>() { framework.ToString() },
                    ConfigFilePaths = _nugetSettings.GetConfigFilePaths(),
                    PackagesPath = SettingsUtility.GetGlobalPackagesFolder(_nugetSettings),
                    Sources = SettingsUtility.GetEnabledSources(_nugetSettings).ToList(),
                    FallbackFolders = SettingsUtility.GetFallbackPackageFolders(_nugetSettings).ToList(),
                },
                FilePath = projectPath,
                Name = Path.GetFileNameWithoutExtension(projectPath),
            };

            DependencyGraphSpec dependencyGraphSpec = new DependencyGraphSpec();

            dependencyGraphSpec.AddProject(packageSpec);

            dependencyGraphSpec.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);

            IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(_restoreCommandProvidersCache, dependencyGraphSpec, _nugetSettings);

            // Create requests from the arguments
            IReadOnlyList<RestoreSummaryRequest> requests = requestProvider.CreateRequests(_restoreArgs).Result;

            // Restore the package without generating extra files
            RestoreResultPair restoreResult = RestoreRunner.RunWithoutCommit(requests, _restoreArgs).Result.First();
            return restoreResult.Result;
        }

        private bool IsPathRootedInRepositoryPath(string path)
        {
            return path != null && path.StartsWith(_repositoryPath, StringComparison.OrdinalIgnoreCase);
        }

        private void ProcessElement(ProjectElement element, Dictionary<PackageInfo, PackageUsage> packageUsages)
        {
            switch (element)
            {
                case ProjectPropertyElement propertyElement:
                    if (PropertiesToRemove.Contains(propertyElement.Name))
                    {
                        Log.LogDebug($"    {element.Location}: Removing property \"{element.ElementName}\"");
                        propertyElement.Remove();
                        return;
                    }

                    break;

                case ProjectItemElement itemElement:
                    if (ItemsToRemove.Contains(itemElement.ItemType) || ItemsToRemove.Contains(itemElement.Include))
                    {
                        Log.LogDebug($"    {element.Location}: Removing item \"{itemElement.ItemType}\"");
                        itemElement.Remove();
                        return;
                    }

                    break;

                case ProjectTargetElement targetElement:
                    if (TargetsToRemove.Contains(targetElement.Name))
                    {
                        Log.LogDebug($"    {element.Location}: Removing target \"{targetElement.Name}\"");
                        targetElement.Remove();
                        return;
                    }

                    break;
            }

            ElementPath elementPath = new ElementPath(element);

            bool elementPathIsValid = false;

            foreach (KeyValuePair<PackageInfo, PackageUsage> kvp in packageUsages)
            {
                PackageInfo package = kvp.Key;
                PackageUsage packageUsage = kvp.Value;
                if (packageUsage.IsMissingTransitiveDependency)
                {
                    continue;
                }

                Match match = GetElementMatch(elementPath, packageUsage);

                if (match != null && match.Success && match.Groups["version"] != null && match.Groups["version"].Success && NuGetVersion.TryParse(match.Groups["version"].Value, out NuGetVersion version))
                {
                    elementPathIsValid = true;

                    if (!version.Equals(package.Identity.Version))
                    {
                        Log.LogWarning($"  {element.Location}: The package version \"{version}\" specified in the \"{element.ElementName}\" element does not match the package version \"{package.Identity.Version}\".  After conversion, the project will reference ONLY version \"{packageUsage.PackageVersion}\"");
                    }
                }
            }

            if (!elementPathIsValid && IsPathRootedInRepositoryPath(elementPath.FullPath))
            {
                bool foundPackage = false;
                foreach (KeyValuePair<PackageInfo, PackageUsage> kvp in packageUsages)
                {
                    PackageInfo package = kvp.Key;
                    PackageUsage packageUsage = kvp.Value;

                    if (!string.IsNullOrEmpty(kvp.Key.RepositoryInstalledPath) && elementPath.FullPath.StartsWith(kvp.Key.RepositoryInstalledPath))
                    {
                        foundPackage = true;
                        string path = Path.Combine(package.GlobalInstalledPath, elementPath.FullPath.Substring(package.RepositoryInstalledPath.Length + 1));

                        if (!File.Exists(path) && !Directory.Exists(path))
                        {
                            Log.LogWarning($"Using path from package which doesn't exist in the package. This needs manual intervention. Path: {elementPath.FullPath}");
                        }
                        else
                        {
                            string rootedPath = path.Substring(_globalPackagesFolder.Length);
                            string[] splitPath =
                                rootedPath.Split(
                                    new[]
                                    {
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar,
                                    },
                                    StringSplitOptions.RemoveEmptyEntries);
                            string relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), splitPath.Skip(2));
                            string generatedProperty = $"$(Pkg{package.Identity.Id.Replace(".", "_")})";
                            path = $"{generatedProperty}\\{relativePath}";

                            packageUsage.GeneratePathProperty = true;
                            elementPath.Set(path);
                        }

                        break;
                    }
                }

                if (!foundPackage)
                {
                    Log.LogWarning($"Package is being used which is not referenced. This needs manual intervention. Usage: {elementPath.FullPath}");
                }
            }
        }

        private void TrimPackages(Dictionary<PackageInfo, PackageUsage> packageUsages, string projectPath, RestoreTargetGraph restoreTargetGraph)
        {
            HashSet<string> nonTopLevelPackages = new(StringComparer.OrdinalIgnoreCase);
            foreach (GraphItem<RemoteResolveResult> item in restoreTargetGraph.Flattened)
            {
                // Skip non-packages, like the project itself which will obviously depend on the top-level packages.
                if (item.Key.Type != LibraryType.Package)
                {
                    continue;
                }

                foreach (LibraryDependency dependency in item.Data.Dependencies)
                {
                    nonTopLevelPackages.Add(dependency.Name);
                }
            }

            HashSet<PackageInfo> packagesToTrim = new();
            foreach (KeyValuePair<PackageInfo, PackageUsage> kvp in packageUsages)
            {
                PackageInfo package = kvp.Key;
                if (nonTopLevelPackages.Contains(package.Identity.Id))
                {
                    Log.LogWarning($"{projectPath}: The transitive package dependency {package.Identity} will be removed because it is referenced by another package in this dependency graph.");
                    packagesToTrim.Add(package);
                }
            }

            foreach (PackageInfo package in packagesToTrim)
            {
                packageUsages.Remove(package);
            }
        }
    }
}
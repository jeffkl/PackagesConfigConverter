// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using log4net;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

// ReSharper disable CollectionNeverUpdated.Local
namespace PackagesConfigConverter
{
    internal sealed class ProjectConverter : IProjectConverter
    {
        private static readonly HashSet<string> ItemsToRemove = new (StringComparer.OrdinalIgnoreCase) { "packages.config" };
        private static readonly HashSet<string> PropertiesToRemove = new (StringComparer.OrdinalIgnoreCase) { "NuGetPackageImportStamp" };
        private static readonly HashSet<string> TargetsToRemove = new (StringComparer.OrdinalIgnoreCase) { "EnsureNuGetPackageBuildImports" };
        private readonly ProjectConverterSettings _converterSettings;
        private readonly string _globalPackagesFolder;
        private readonly ISettings _nugetSettings;
        private readonly ProjectCollection _projectCollection = new ();
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

            Log.Info($"  NuGet configuration file : \"{_converterSettings.NuGetConfigPath}\"");

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

                string packagesConfigPath = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, "packages.config");

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
                converterSettings.NuGetConfigPath = nugetConfigPath;

                return Settings.LoadSpecificSettings(converterSettings.RepositoryRoot, Settings.DefaultSettingsFileName);
            }

            return Settings.LoadDefaultSettings(converterSettings.RepositoryRoot, Settings.DefaultSettingsFileName, new XPlatMachineWideSetting());
        }

        private ProjectItemElement AddPackageReference(ProjectItemGroupElement itemGroupElement, PackageReference package)
        {
            LibraryIncludeFlags includeAssets = LibraryIncludeFlags.All;
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

            if (package.IsMissingTransitiveDependency)
            {
                includeAssets = LibraryIncludeFlags.None;
                excludeAssets = LibraryIncludeFlags.None;
                privateAssets = LibraryIncludeFlags.All;
            }

            ProjectItemElement itemElement = itemGroupElement.AppendItem("PackageReference", package.PackageIdentity.Id);

            itemElement.AddMetadataAsAttribute("Version", package.PackageVersion.ToNormalizedString());

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

            if (package.GeneratePathProperty)
            {
                itemElement.AddMetadataAsAttribute("GeneratePathProperty", bool.TrueString);
            }

            return itemElement;
        }

        private bool ConvertProject(string projectPath, string packagesConfigPath)
        {
            try
            {
                Log.Info($"  Converting project \"{projectPath}\"");

                PackagesConfigReader packagesConfigReader = new PackagesConfigReader(XDocument.Load(packagesConfigPath));

                List<PackageReference> packages = packagesConfigReader.GetPackages(allowDuplicatePackageIds: true).Select(i => new PackageReference(i.PackageIdentity, i.TargetFramework, i.IsUserInstalled, i.IsDevelopmentDependency, i.RequireReinstallation, i.AllowedVersions, PackagePathResolver, VersionFolderPathResolver)).ToList();

                DetectMissingTransitiveDependencies(packages, projectPath);

                ProjectRootElement project = ProjectRootElement.Open(projectPath, _projectCollection, preserveFormatting: true);

                ProjectItemGroupElement packageReferenceItemGroupElement = null;

                foreach (ProjectElement element in project.AllChildren)
                {
                    ProcessElement(element, packages);

                    if (packageReferenceItemGroupElement == null && element is ProjectItemElement { ItemType: "Reference" })
                    {
                        // Find the first Reference item and use it to add PackageReference items to, otherwise a new ItemGroup is added
                        packageReferenceItemGroupElement = element.Parent as ProjectItemGroupElement;
                    }
                }

                packageReferenceItemGroupElement ??= project.AddItemGroup();

                Log.Info("    Current package references:");

                foreach (PackageReference package in packages.Where(i => !i.IsMissingTransitiveDependency))
                {
                    Log.Info($"      {package.PackageIdentity}");
                }

                foreach (ProjectElement element in packages.Where(i => !i.IsMissingTransitiveDependency).SelectMany(i => i.AllElements))
                {
                    Log.Debug($"    {element.Location}: Removing element {element.ToXmlString()}");
                    element.Remove();
                }

                if (_converterSettings.TrimPackages)
                {
                    List<NuGetFramework> targetFrameworks = new List<NuGetFramework>
                    {
                        FrameworkConstants.CommonFrameworks.Net45,
                    };

                    using SourceCacheContext sourceCacheContext = new SourceCacheContext
                    {
                        IgnoreFailedSources = true,
                    };

                    RestoreTargetGraph packageRestoreGraph = GetRestoreTargetGraph(packages, projectPath, targetFrameworks, sourceCacheContext);
                    TrimPackages(packages, projectPath, packageRestoreGraph.Flattened);
                }

                Log.Info("    Converted package references:");

                foreach (PackageReference package in packages)
                {
                    ProjectItemElement packageReferenceItemElement = AddPackageReference(packageReferenceItemGroupElement, package);

                    Log.Info($"      {packageReferenceItemElement.ToXmlString()}");
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

        private void DetectMissingTransitiveDependencies(List<PackageReference> packages, string projectPath)
        {
            List<NuGetFramework> targetFrameworks = new List<NuGetFramework>
            {
                FrameworkConstants.CommonFrameworks.Net45,
            };

            using SourceCacheContext sourceCacheContext = new SourceCacheContext
            {
                IgnoreFailedSources = true,
            };

            RestoreTargetGraph restoreTargetGraph = GetRestoreTargetGraph(packages, projectPath, targetFrameworks, sourceCacheContext);

            if (restoreTargetGraph != null)
            {
                IEnumerable<GraphItem<RemoteResolveResult>> flatPackageDependencies = restoreTargetGraph.Flattened.Where(i => i.Key.Type == LibraryType.Package);
                foreach (GraphItem<RemoteResolveResult> packageDependency in flatPackageDependencies)
                {
                    (LibraryIdentity library, _) = (packageDependency.Key, packageDependency.Data);
                    PackageReference packageReference = packages.FirstOrDefault(i => i.PackageId.Equals(library.Name, StringComparison.OrdinalIgnoreCase));

                    if (packageReference == null)
                    {
                        Log.Warn($"{projectPath}: The transitive package dependency \"{library.Name} {library.Version}\" was not in the packages.config.  After converting to PackageReference, new dependencies will be pulled in transitively which could lead to restore or build errors");

                        packages.Add(new PackageReference(new PackageIdentity(library.Name, library.Version), NuGetFramework.AnyFramework, true, false, true, new VersionRange(library.Version), PackagePathResolver, VersionFolderPathResolver)
                        {
                            IsMissingTransitiveDependency = true,
                        });
                    }
                }
            }
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

        private RestoreTargetGraph GetRestoreTargetGraph(List<PackageReference> packages, string projectPath, List<NuGetFramework> targetFrameworks, SourceCacheContext sourceCacheContext)
        {
            // The package spec details what packages to restore
            PackageSpec packageSpec = new PackageSpec(targetFrameworks.Select(i => new TargetFrameworkInformation
            {
                FrameworkName = i,
            }).ToList())
            {
                Dependencies = packages.Select(i => new LibraryDependency
                {
                    LibraryRange = new LibraryRange(i.PackageId, new VersionRange(i.PackageVersion), LibraryDependencyTarget.Package),
                }).ToList(),
                RestoreMetadata = new ProjectRestoreMetadata
                {
                    ProjectPath = projectPath,
                    ProjectName = Path.GetFileNameWithoutExtension(projectPath),
                    ProjectStyle = ProjectStyle.PackageReference,
                    ProjectUniqueName = projectPath,
                    OutputPath = Path.GetTempPath(),
                    OriginalTargetFrameworks = targetFrameworks.Select(i => i.ToString()).ToList(),
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

            IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), dependencyGraphSpec);

            RestoreArgs restoreArgs = new RestoreArgs
            {
                AllowNoOp = true,
                CacheContext = sourceCacheContext,
                CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(_nugetSettings)),
                Log = NullLogger.Instance,
            };

            // Create requests from the arguments
            IReadOnlyList<RestoreSummaryRequest> requests = requestProvider.CreateRequests(restoreArgs).Result;

            // Restore the package without generating extra files
            RestoreResultPair restoreResult = RestoreRunner.RunWithoutCommit(requests, restoreArgs).Result.FirstOrDefault();

            RestoreTargetGraph restoreTargetGraph = restoreResult?.Result.RestoreGraphs.FirstOrDefault();
            return restoreTargetGraph;
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

            foreach (PackageReference package in packages.Where(i => !i.IsMissingTransitiveDependency))
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
                PackageReference package = packages.FirstOrDefault(i => !string.IsNullOrEmpty(i.RepositoryInstalledPath) && elementPath.FullPath.StartsWith(i.RepositoryInstalledPath));

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
                        string generatedProperty = $"$(Pkg{package.PackageId.Replace(".", "_")})";
                        path = $"{generatedProperty}\\{relativePath}";

                        package.GeneratePathProperty = true;
                        elementPath.Set(path);
                    }
                }
            }
        }

        private void TrimPackages(List<PackageReference> packages, string projectPath, ICollection<GraphItem<RemoteResolveResult>> flatPackageDependencies)
        {
            IEnumerable<LibraryDependency> GetPackageDependencies(PackageReference package) => flatPackageDependencies.First(p => p.Key.Name.Equals(package.PackageId, StringComparison.OrdinalIgnoreCase)).Data.Dependencies;

            bool IsPackageInDependencySet(PackageReference packageReference, IEnumerable<LibraryDependency> dependencies) => dependencies.Any(d => d.Name.Equals(packageReference.PackageId, StringComparison.OrdinalIgnoreCase));

            packages.RemoveAll(package =>
            {
                bool willRemove = packages.Select(GetPackageDependencies).Any(dependencies => IsPackageInDependencySet(package, dependencies));

                if (willRemove)
                {
                    Log.Warn($"{projectPath}: The transitive package dependency {package.PackageId} {package.AllowedVersions} will be removed because it is referenced by another package in this dependency graph.");
                }

                return willRemove;
            });
        }
    }
}
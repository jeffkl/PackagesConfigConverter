﻿using log4net;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
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
namespace PackagesConfigProjectConverter
{
    internal sealed class ProjectConverter : IProjectConverter
    {
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

                    if (packageReferenceItemGroupElement == null && element is ProjectItemElement itemElement && itemElement.ItemType.Equals("Reference"))
                    {
                        // Find the first Reference item and use it to add PackageReference items to, otherwise a new ItemGroup is added
                        packageReferenceItemGroupElement = element.Parent as ProjectItemGroupElement;
                    }
                }

                if (packageReferenceItemGroupElement == null)
                {
                    packageReferenceItemGroupElement = project.AddItemGroup();
                }

                Log.Info("    Current package references:");

                foreach (PackageReference package in packages.Where(i => !i.IsMissingTransitiveDependency))
                {
                    Log.Info($"      {package.PackageIdentity}");
                }

                Log.Info("    Converted package references:");

                foreach (PackageReference package in packages)
                {
                    ProjectItemElement packageReferenceItemElement = AddPackageReference(packageReferenceItemGroupElement, package);

                    Log.Info($"      {packageReferenceItemElement.ToXmlString()}");
                }

                foreach (ProjectElement element in packages.Where(i => !i.IsMissingTransitiveDependency).SelectMany(i => i.AllElements))
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

        private void DetectMissingTransitiveDependencies(List<PackageReference> packages, string projectPath)
        {
            List<NuGetFramework> targetFrameworks = new List<NuGetFramework>
            {
                FrameworkConstants.CommonFrameworks.Net45
            };

            using (SourceCacheContext sourceCacheContext = new SourceCacheContext
            {
                IgnoreFailedSources = true,
            })
            {
                // The package spec details what packages to restore
                PackageSpec packageSpec = new PackageSpec(targetFrameworks.Select(i => new TargetFrameworkInformation
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

                if (restoreTargetGraph != null)
                {
                    foreach (LibraryIdentity library in restoreTargetGraph.Flattened.Where(i => i.Key.Type == LibraryType.Package).Select(i => i.Key))
                    {
                        PackageReference packageReference = packages.FirstOrDefault(i => i.PackageId.Equals(library.Name, StringComparison.OrdinalIgnoreCase));

                        if (packageReference == null)
                        {
                            Log.Warn($"{projectPath}: The transitive package dependency \"{library.Name} {library.Version}\" was not in the packages.config.  After converting to PackageReference, new dependencies will be pulled in transitively which could lead to restore or build errors");

                            packages.Add(new PackageReference(new PackageIdentity(library.Name, library.Version), NuGetFramework.AnyFramework, true, false, true, new VersionRange(library.Version), PackagePathResolver, VersionFolderPathResolver)
                            {
                                IsMissingTransitiveDependency = true
                            });
                        }
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
                        path = $"$(NuGetPackageRoot){path.Substring(_globalPackagesFolder.Length)}";

                        elementPath.Set(path);
                    }
                }
            }
        }
    }
}
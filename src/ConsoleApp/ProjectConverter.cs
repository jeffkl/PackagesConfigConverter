using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConsoleApp
{
    internal sealed class ProjectConverter : IDisposable
    {
        private const string CommonPackagesGroupLabel = "Package versions used by this repository";

        // ReSharper disable once CollectionNeverUpdated.Local
        private static readonly AssemblyReferenceRegularExpressions AssemblyReferenceRegularExpressions = new AssemblyReferenceRegularExpressions();

        // ReSharper disable once CollectionNeverUpdated.Local
        private static readonly ImportRegularExpressions ImportRegularExpressions = new ImportRegularExpressions();

        private static readonly string[] ItemsToRemove = { "packages.config" };
        private static readonly string[] ItemTypesToRemove = { "Analyzer" };
        private static readonly string[] PropertiesToRemove = { "NuGetPackageImportStamp" };
        private readonly ProjectCollection _projectCollection;
        private readonly string _packagesRoot;
        private readonly string _repoRoot;

        public ProjectConverter(ProjectCollection projectCollection, string packagesRoot, string repoRoot, bool usePackagesProps)
        {
            _projectCollection = projectCollection ?? throw new ArgumentNullException(nameof(projectCollection));
            _packagesRoot = packagesRoot ?? throw new ArgumentNullException(nameof(packagesRoot));
            _repoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));

            if (usePackagesProps)
            {
                if (GetPackagesPropsOrNull() == null)
                {
                    throw new InvalidOperationException($"{nameof(ProgramArguments.UsePackagesProps)} command line option is 'true' but packages.props is not found in {repoRoot}");
                }
            }
        }

        public void ConvertProject(string projectPath)
        {
            string packagesConfigPath = Path.Combine(Path.GetDirectoryName(projectPath), "packages.config");

            if (!File.Exists(packagesConfigPath))
            {
                return;
            }

            ProjectRootElement packagesProps = GetPackagesPropsOrNull();

            PackagesConfigReader packagesConfigReader = new PackagesConfigReader(XDocument.Load(packagesConfigPath));

            List<PackageIdentity> packages = packagesConfigReader.GetPackages(allowDuplicatePackageIds: true).Select(i => i.PackageIdentity).ToList();

            ProjectRootElement project = ProjectRootElement.Open(projectPath, _projectCollection, preserveFormatting: true);

            try
            {
                RemoveImports(project, packages);

                RemoveTargets(project);

                RemoveProperties(project);

                RemoveItems(project);

                ReplaceReferences(project, packages, packagesProps);

                project.Save();

                if (packagesProps != null)
                {
                    packagesProps.Save();
                }

                File.Delete(packagesConfigPath);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to convert '{projectPath}' : {e}");
            }
        }

        private ProjectRootElement GetPackagesPropsOrNull()
        {
            string packagesPropsFile = Path.Combine(_repoRoot, "packages.props");
            if (File.Exists(packagesPropsFile))
            {
                return ProjectRootElement.Open(packagesPropsFile, _projectCollection, preserveFormatting: true);
            }

            return null;
        }

        public void ConvertRepository(string repositoryPath, string exclude, string include)
        {
            Regex excludeRegex = CreateFileFilterRegexOrNull(exclude);
            Regex includeRegex = CreateFileFilterRegexOrNull(include);

            foreach (string file in Directory.EnumerateFiles(repositoryPath, "*.csproj", SearchOption.AllDirectories)
                .Where(f => excludeRegex == null || !excludeRegex.IsMatch(f))
                .Where(f => includeRegex == null || includeRegex.IsMatch(f)))
            {
                try
                {
                    ConvertProject(file);
                }
                catch(InvalidDataException e)
                {
                    Console.WriteLine($"[ERROR] Failed to convert {file} : {e}");
                }
            }
        }

        private Regex CreateFileFilterRegexOrNull(string expression)
        {
            if (expression == null)
            {
                return null;
            }

            return new Regex(expression, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public void Dispose()
        {
            _projectCollection?.Dispose();
        }

        private void RemoveImports(ProjectRootElement project, List<PackageIdentity> packages)
        {
            var importsToRemove = new List<ProjectImportElement>();

            foreach (ProjectImportElement importElement in project.Imports)
            {
                foreach (PackageIdentity package in packages)
                {
                    Regex regex = ImportRegularExpressions[package];

                    if (regex.IsMatch(importElement.Project))
                    {
                        importsToRemove.Add(importElement);
                    }
                }
            }

            foreach (ProjectImportElement projectImportElement in importsToRemove)
            {
                projectImportElement.Parent.RemoveChild(projectImportElement);
            }
        }

        private void RemoveItems(ProjectRootElement project)
        {
            foreach (string itemSpec in ItemsToRemove)
            {
                foreach (ProjectItemElement itemElement in project.Items.Where(i => i.Include.Equals(itemSpec, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    if (itemElement.Parent.Count == 1)
                    {
                        itemElement.Parent.Parent.RemoveChild(itemElement.Parent);
                    }
                    else
                    {
                        itemElement.Parent.RemoveChild(itemElement);
                    }
                }
            }

            foreach (string itemType in ItemTypesToRemove)
            {
                foreach (ProjectItemElement itemElement in project.Items.Where(i => i.ItemType.Equals(itemType, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    if (itemElement.Parent.Count == 1)
                    {
                        itemElement.Parent.Parent.RemoveChild(itemElement.Parent);
                    }
                    else
                    {
                        itemElement.Parent.RemoveChild(itemElement);
                    }
                }
            }
        }

        private void RemoveProperties(ProjectRootElement project)
        {
            foreach (string propertyName in PropertiesToRemove)
            {
                foreach (ProjectPropertyElement propertyElement in project.Properties.Where(i => i.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    propertyElement.Parent.RemoveChild(propertyElement);
                }
            }
        }

        private void RemoveTargets(ProjectRootElement project)
        {
            foreach (ProjectTargetElement targetElement in project.Targets.Where(i => i.Name.Equals("EnsureNuGetPackageBuildImports", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                targetElement.Parent.RemoveChild(targetElement);
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
                    Regex regex = AssemblyReferenceRegularExpressions[packageIdentity];

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
                .FirstOrDefault(i =>    i.ItemType.Equals("PackageVersion", StringComparison.OrdinalIgnoreCase) && 
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
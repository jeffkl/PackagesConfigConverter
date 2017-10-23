using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Packaging;
using NuGet.Packaging.Core;
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
        // ReSharper disable once CollectionNeverUpdated.Local
        private static readonly AssemblyReferenceRegularExpressions AssemblyReferenceRegularExpressions = new AssemblyReferenceRegularExpressions();

        // ReSharper disable once CollectionNeverUpdated.Local
        private static readonly ImportRegularExpressions ImportRegularExpressions = new ImportRegularExpressions();

        private static readonly string[] ItemsToRemove = {"packages.config"};
        private static readonly string[] ItemTypesToRemove = {"Analyzer"};
        private static readonly string[] PropertiesToRemove = {"NuGetPackageImportStamp"};
        private readonly ProjectCollection _projectCollection;

        public ProjectConverter()
            : this(new ProjectCollection())
        {
        }

        public ProjectConverter(ProjectCollection projectCollection)
        {
            _projectCollection = projectCollection ?? throw new ArgumentNullException(nameof(projectCollection));
        }

        public void ConvertProject(string projectPath)
        {
            string packagesConfigPath = Path.Combine(Path.GetDirectoryName(projectPath), "packages.config");

            if (!File.Exists(packagesConfigPath))
            {
                return;
            }

            PackagesConfigReader packagesConfigReader = new PackagesConfigReader(XDocument.Load(packagesConfigPath));

            List<PackageIdentity> packages = packagesConfigReader.GetPackages(allowDuplicatePackageIds: true).Select(i => i.PackageIdentity).ToList();

            ProjectRootElement project = ProjectRootElement.Open(projectPath, _projectCollection, preserveFormatting: true);

            try
            {
                RemoveImports(project, packages);

                RemoveTargets(project);

                RemoveProperties(project);

                RemoveItems(project);

                ReplaceReferences(project, packages);

                project.Save();

                File.Delete(packagesConfigPath);
            }
            catch (Exception)
            {
                Console.WriteLine($"Failed to convert '{projectPath}'");
            }
        }

        public void ConvertRepository(string repositoryPath, IEnumerable<string> exclusions = null)
        {
            HashSet<string> exlustionsHashSet = new HashSet<string>(exclusions ?? Enumerable.Empty<string>());

            foreach (string file in Directory.EnumerateFiles(repositoryPath, "*.csproj", SearchOption.AllDirectories).Where(i => !exlustionsHashSet.Any(e => i.StartsWith(e, StringComparison.OrdinalIgnoreCase))))
            {
                ConvertProject(file);
            }
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

        private void ReplaceReferences(ProjectRootElement project, List<PackageIdentity> packages)
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

            ProjectItemElement lastItem = project.Items.First(i => i.ItemType.Equals("Reference")) ?? project.ItemGroups.First().Items.First();

            foreach (KeyValuePair<ProjectItemElement, PackageIdentity> pair in itemsToReplace)
            {
                if (!packagesAdded.Contains(pair.Value))
                {
                    ProjectItemElement item = project.CreateItemElement("PackageReference", pair.Value.Id);

                    pair.Key.Parent.InsertAfterChild(item, pair.Key);

                    item.AddMetadata("Version", pair.Value.Version.ToString());

                    packagesAdded.Add(pair.Value);

                    lastItem = item;
                }

                pair.Key.Parent.RemoveChild(pair.Key);
            }

            foreach (PackageIdentity package in allPackages)
            {
                if (lastItem == null)
                {
                    var itemGroup = project.AddItemGroup();

                    lastItem = itemGroup.AddItem("PackageReference", package.Id, new List<KeyValuePair<string, string>> {new KeyValuePair<string, string>("Version", package.Version.ToString())});
                }
                else
                {
                    ProjectItemElement item = project.CreateItemElement("PackageReference", package.Id);

                    lastItem.Parent.InsertAfterChild(item, lastItem);

                    item.AddMetadata("Version", package.Version.ToString());

                    lastItem = item;
                }
            }
        }
    }
}
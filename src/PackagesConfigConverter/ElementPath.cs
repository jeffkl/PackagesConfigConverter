// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using Microsoft.Build.Construction;
using System;

namespace PackagesConfigConverter
{
    internal class ElementPath
    {
        public ElementPath(ProjectElement element)
        {
            Element = element ?? throw new ArgumentNullException(nameof(element));

            switch (element)
            {
                case ProjectItemElement itemElement:
                    OriginalPath = itemElement.Include;

                    if (itemElement.ItemType.Equals("Reference"))
                    {
                        OriginalPath = itemElement.Metadata.Value("HintPath");

                        if (!string.IsNullOrWhiteSpace(OriginalPath))
                        {
                            HintPath = true;
                        }
                    }

                    break;

                case ProjectImportElement importElement:
                    OriginalPath = importElement.Project;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(OriginalPath))
            {
                FullPath = element.ContainingProject.GetProjectFullPath(OriginalPath);
            }
        }

        public ProjectElement Element { get; }

        public string FullPath { get; }

        public bool HintPath { get; }

        public string OriginalPath { get; }

        public void Set(string path)
        {
            switch (Element)
            {
                case ProjectItemElement itemElement:

                    if (HintPath)
                    {
                        itemElement.SetMetadata("HintPath", path);
                    }
                    else
                    {
                        itemElement.Include = path;
                    }

                    break;

                case ProjectImportElement importElement:
                    importElement.Project = path;
                    break;

                default:
                    throw new NotSupportedException($"{Element.GetType()}");
            }
        }
    }
}
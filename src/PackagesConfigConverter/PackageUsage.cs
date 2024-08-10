// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using NuGet.LibraryModel;
using NuGet.Versioning;

namespace PackagesConfigConverter
{
    internal sealed class PackageUsage
    {
        public PackageUsage(PackageInfo packageInfo)
        {
            PackageInfo = packageInfo;
        }

        public PackageInfo PackageInfo { get; }

        public LibraryIncludeFlags UsedAssets { get; set; }

        public bool GeneratePathProperty { get; set; }

        public bool IsMissingTransitiveDependency { get; set; }

        public string PackageId => PackageInfo.Identity.Id;

        public NuGetVersion PackageVersion => PackageInfo.Identity.Version;
    }
}
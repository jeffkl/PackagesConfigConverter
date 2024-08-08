// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System.Text.RegularExpressions;
using NuGet.Packaging.Core;

namespace PackagesConfigConverter
{
    internal class ImportRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override string GetRegularExpression(PackageIdentity packageIdentity)
        {
            return $@".*\\{Regex.Escape(packageIdentity.Id)}\.{SemVerPattern}\\build\\.*{Regex.Escape(packageIdentity.Id)}\.(?:props|targets)";
        }
    }
}
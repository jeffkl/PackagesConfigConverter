// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using NuGet.Packaging.Core;
using System.Text.RegularExpressions;

namespace PackagesConfigProjectConverter
{
    internal class AssemblyReferenceRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override string GetRegularExpression(PackageIdentity packageIdentity)
        {
            return $@".*\\{Regex.Escape(packageIdentity.Id)}\.{SemVerPattern}\\lib\\.*";
        }
    }
}
// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

namespace PackagesConfigProjectConverter
{
    internal static class RegularExpressions
    {
        public static AnalyzerRegularExpressions Analyzers { get; } = new AnalyzerRegularExpressions();

        public static AssemblyReferenceRegularExpressions AssemblyReferences { get; } = new AssemblyReferenceRegularExpressions();

        public static ImportRegularExpressions Imports { get; } = new ImportRegularExpressions();
    }
}
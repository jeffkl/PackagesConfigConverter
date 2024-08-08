// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

namespace PackagesConfigConverter
{
    internal static class RegularExpressions
    {
        public static AnalyzerRegularExpressions Analyzers { get; } = new();

        public static AssemblyReferenceRegularExpressions AssemblyReferences { get; } = new();

        public static ImportRegularExpressions Imports { get; } = new();
    }
}
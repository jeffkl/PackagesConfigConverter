// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;

namespace PackagesConfigProjectConverter
{
    public static class ProjectConverterFactory
    {
        internal static Func<ProjectConverterSettings, IProjectConverter> Creator { get; set; } = settings => new ProjectConverter(settings);

        public static IProjectConverter Create(ProjectConverterSettings settings)
        {
            return Creator(settings);
        }
    }
}
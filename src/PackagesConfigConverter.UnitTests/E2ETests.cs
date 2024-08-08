// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace PackagesConfigConverter.UnitTests
{
    public class E2ETests : TestBase
    {
        private const string BaseTestCaseDir = @"TestData\E2ETests";
        private const string BaseWorkingDir = @"_work";
        private const string NuGetConfigFile = "NuGet.config";
        private const string PackagesConfigFileName = "packages.config";
        private const string BeforeProjectName = "before.csproj";
        private const string AfterProjectName = "after.csproj";

        private static readonly Regex ConverterInclude = new Regex($"{BeforeProjectName}$", RegexOptions.Compiled);

        public E2ETests(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        {
        }

        public static IEnumerable<object[]> TestCases()
        {
            foreach (string dir in Directory.EnumerateDirectories(BaseTestCaseDir))
            {
                string testCase = Path.GetFileName(dir);
                yield return new object[] { testCase };
            }
        }

        [Theory]
        [MemberData(nameof(TestCases))]
        public void E2ETest(string testCase)
        {
            // Copy test files to a working dir
            string testCaseDir = Path.Combine(BaseTestCaseDir, testCase);
            string workingDir = Path.Combine(BaseWorkingDir, testCase);

            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }

            Directory.CreateDirectory(workingDir);
            foreach (string file in Directory.EnumerateFiles(testCaseDir, "*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(testCaseDir.Length + 1);
                string destintion = Path.Combine(workingDir, relativePath);
                File.Copy(file, destintion);
            }

            File.Copy(Path.Combine(BaseTestCaseDir, NuGetConfigFile), Path.Combine(workingDir, NuGetConfigFile));

            string packagesConfigFile = Path.Combine(workingDir, PackagesConfigFileName);
            Assert.True(File.Exists(packagesConfigFile));

            // Restore the project
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "nuget.exe",
                    Arguments = "restore",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };

            process.OutputDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    TestOutputHelper.WriteLine(eventArgs.Data);
                }
            };

            process.ErrorDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    TestOutputHelper.WriteLine(eventArgs.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);

            // Run the conversion
            var converterSettings = new ProjectConverterSettings()
            {
                Include = ConverterInclude,
                Log = new XUnitLogger(TestOutputHelper),
                RepositoryRoot = workingDir,
            };
            var converter = new ProjectConverter(converterSettings);
            converter.ConvertRepository(CancellationToken.None);

            // File was deleted
            Assert.False(File.Exists(packagesConfigFile));

            string expectedProjectContent = File.ReadAllText(Path.Combine(testCaseDir, AfterProjectName));
            string actualProjectContent = File.ReadAllText(Path.Combine(workingDir, BeforeProjectName));
            Assert.Equal(expectedProjectContent, actualProjectContent);
        }
    }
}
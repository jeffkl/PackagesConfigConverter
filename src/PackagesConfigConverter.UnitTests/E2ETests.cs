// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using log4net;
using log4net.Core;
using System;
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
                Log = new TestLog(TestOutputHelper),
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

        private sealed class TestLog : ILog
        {
            private const string DebugLevel = "Debug";
            private const string ErrorLevel = "Error";
            private const string FatalLevel = "Fatal";
            private const string InfoLevel = "Info";
            private const string WarnLevel = "Warn";

            private readonly ITestOutputHelper _testOutputHelper;

            public TestLog(ITestOutputHelper testOutputHelper)
            {
                _testOutputHelper = testOutputHelper;
            }

            public bool IsDebugEnabled => true;

            public bool IsInfoEnabled => true;

            public bool IsWarnEnabled => true;

            public bool IsErrorEnabled => true;

            public bool IsFatalEnabled => true;

            public ILogger Logger => throw new NotImplementedException();

            public void Debug(object message) => Log(DebugLevel, message);

            public void Debug(object message, Exception exception) => Log(DebugLevel, message, exception);

            public void DebugFormat(string format, params object[] args) => Log(DebugLevel, format, args);

            public void DebugFormat(string format, object arg0) => Log(DebugLevel, string.Format(format, arg0));

            public void DebugFormat(string format, object arg0, object arg1) => Log(DebugLevel, string.Format(format, arg0, arg1));

            public void DebugFormat(string format, object arg0, object arg1, object arg2) => Log(DebugLevel, string.Format(format, arg0, arg2));

            public void DebugFormat(IFormatProvider provider, string format, params object[] args) => Log(DebugLevel, string.Format(provider, format, args));

            public void Error(object message) => Log(ErrorLevel, message);

            public void Error(object message, Exception exception) => Log(ErrorLevel, message, exception);

            public void ErrorFormat(string format, params object[] args) => Log(ErrorLevel, format, args);

            public void ErrorFormat(string format, object arg0) => Log(ErrorLevel, string.Format(format, arg0));

            public void ErrorFormat(string format, object arg0, object arg1) => Log(ErrorLevel, string.Format(format, arg0, arg1));

            public void ErrorFormat(string format, object arg0, object arg1, object arg2) => Log(ErrorLevel, string.Format(format, arg0, arg2));

            public void ErrorFormat(IFormatProvider provider, string format, params object[] args) => Log(ErrorLevel, string.Format(provider, format, args));

            public void Fatal(object message) => Log(FatalLevel, message);

            public void Fatal(object message, Exception exception) => Log(FatalLevel, message, exception);

            public void FatalFormat(string format, params object[] args) => Log(FatalLevel, format, args);

            public void FatalFormat(string format, object arg0) => Log(FatalLevel, string.Format(format, arg0));

            public void FatalFormat(string format, object arg0, object arg1) => Log(FatalLevel, string.Format(format, arg0, arg1));

            public void FatalFormat(string format, object arg0, object arg1, object arg2) => Log(FatalLevel, string.Format(format, arg0, arg2));

            public void FatalFormat(IFormatProvider provider, string format, params object[] args) => Log(FatalLevel, string.Format(provider, format, args));

            public void Info(object message) => Log(InfoLevel, message);

            public void Info(object message, Exception exception) => Log(InfoLevel, message, exception);

            public void InfoFormat(string format, params object[] args) => Log(InfoLevel, format, args);

            public void InfoFormat(string format, object arg0) => Log(InfoLevel, string.Format(format, arg0));

            public void InfoFormat(string format, object arg0, object arg1) => Log(InfoLevel, string.Format(format, arg0, arg1));

            public void InfoFormat(string format, object arg0, object arg1, object arg2) => Log(InfoLevel, string.Format(format, arg0, arg2));

            public void InfoFormat(IFormatProvider provider, string format, params object[] args) => Log(InfoLevel, string.Format(provider, format, args));

            public void Warn(object message) => Log(WarnLevel, message);

            public void Warn(object message, Exception exception) => Log(WarnLevel, message, exception);

            public void WarnFormat(string format, params object[] args) => Log(WarnLevel, format, args);

            public void WarnFormat(string format, object arg0) => Log(WarnLevel, string.Format(format, arg0));

            public void WarnFormat(string format, object arg0, object arg1) => Log(WarnLevel, string.Format(format, arg0, arg1));

            public void WarnFormat(string format, object arg0, object arg1, object arg2) => Log(WarnLevel, string.Format(format, arg0, arg2));

            public void WarnFormat(IFormatProvider provider, string format, params object[] args) => Log(WarnLevel, string.Format(provider, format, args));

            private void Log(string level, object message) => _testOutputHelper.WriteLine($"[{level}] {message}");

            private void Log(string level, object message, Exception exception) => _testOutputHelper.WriteLine($"[{level}] {message}. Exception: {exception}");

            private void Log(string level, string format, params object[] args) => _testOutputHelper.WriteLine($"[{level}] {format}", args);
        }
    }
}

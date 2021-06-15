// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace PackagesConfigConverter.UnitTests
{
    public class ProgramTests : TestBase
    {
        public ProgramTests(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        {
        }

        [Fact]
        public void ArgumentsArePassedCorrectly()
        {
            MockProjectConverter projectConverter = null;

            ProjectConverterFactory.Creator = settings =>
            {
                projectConverter = new MockProjectConverter(settings);

                return projectConverter;
            };

            ConfigureConsole("Y");

            Program.Main(new[]
            {
                "-r", "8EE25E30FEE249FD8983D1B48E9D0ED7",
                "-i", "0984A6D1A29C444F83DB7C8107A19358",
                "-e", "8ED906CD071844EB8F8403E008389044",
                "-y",
            });

            projectConverter.ShouldNotBeNull();

            projectConverter.Settings.RepositoryRoot.ShouldBe("8EE25E30FEE249FD8983D1B48E9D0ED7");
            projectConverter.Settings.Include.ToString().ShouldBe("0984A6D1A29C444F83DB7C8107A19358");
            projectConverter.Settings.Exclude.ToString().ShouldBe("8ED906CD071844EB8F8403E008389044");
        }

        [Fact]
        public void InvalidArguments()
        {
            ConfigureConsole();
            Program.Main(new[] { "--A28DFF0D2D3D45428F8A3A161CB94117" }).ShouldBe(1);

            TestOutputHelper.Output.ShouldContain("Option 'A28DFF0D2D3D45428F8A3A161CB94117' is unknown.");
        }
    }
}
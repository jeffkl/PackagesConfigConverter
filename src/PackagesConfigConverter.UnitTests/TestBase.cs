// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace PackagesConfigConverter.UnitTests
{
    public abstract class TestBase : IDisposable
    {
        private TestOutputHandlerConsole _console;

        protected TestBase(ITestOutputHelper testOutputHelper)
        {
            TestOutputHelper = testOutputHelper as TestOutputHelper;
        }

        protected TestOutputHelper TestOutputHelper { get; }

        public virtual void Dispose()
        {
        }

        protected void ConfigureConsole(string readLine = null)
        {
            _console = new TestOutputHandlerConsole(TestOutputHelper, readLine);

            Console.SetOut(_console.Out);
            Console.SetError(_console.Error);
            Console.SetIn(_console.In);
        }
    }
}
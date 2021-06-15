// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace PackagesConfigConverter.UnitTests
{
    public class TestOutputHandlerConsole
    {
        public TestOutputHandlerConsole(ITestOutputHelper helper, string readLine)
        {
            Out = new TestOutputHelperTextWriter(helper, "STDOUT: ");
            Error = new TestOutputHelperTextWriter(helper, "STDERR: ");
            In = new TestTextReader(readLine);
        }

        public TextWriter Error { get; }

        public TextReader In { get; }

        public TextWriter Out { get; }

        private class TestOutputHelperTextWriter : TextWriter
        {
            private readonly string _prefix;
            private readonly ITestOutputHelper _testOutputHelper;

            public TestOutputHelperTextWriter(ITestOutputHelper testOutputHelper, string prefix)
            {
                _testOutputHelper = testOutputHelper ?? throw new ArgumentNullException(nameof(testOutputHelper));
                _prefix = prefix;
            }

            public override Encoding Encoding => Encoding.Default;

            public override void Write(string value)
            {
                WriteLine(value);
            }

            public override void WriteLine(string message)
            {
                _testOutputHelper.WriteLine($"{_prefix}{message}");
            }

            public override void WriteLine(string format, params object[] args)
            {
                _testOutputHelper.WriteLine($"{_prefix}{format}", args);
            }
        }

        private class TestTextReader : TextReader
        {
            private readonly string _readLine;

            public TestTextReader(string readLine)
            {
                _readLine = readLine;
            }

            public override string ReadLine()
            {
                if (_readLine == null)
                {
                    throw new InvalidOperationException();
                }

                return _readLine;
            }
        }
    }
}
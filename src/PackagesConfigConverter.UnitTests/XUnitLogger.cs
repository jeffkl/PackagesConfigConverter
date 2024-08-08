// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace PackagesConfigConverter.UnitTests
{
    internal sealed class XUnitLogger : ILogger
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public XUnitLogger(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => _testOutputHelper.WriteLine(formatter(state, exception));
    }
}
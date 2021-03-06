﻿// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.Threading;

namespace PackagesConfigConverter
{
    public interface IProjectConverter : IDisposable
    {
        void ConvertRepository(CancellationToken cancellationToken);
    }
}
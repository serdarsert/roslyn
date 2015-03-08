﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis
{
    internal abstract partial class CommonCompiler
    {
        internal sealed class LoggingSourceFileResolver : SourceFileResolver
        {
            private readonly TouchedFileLogger _logger;

            public LoggingSourceFileResolver(ImmutableArray<string> searchPaths, string baseDirectory, TouchedFileLogger logger)
                : base(searchPaths, baseDirectory)
            {
                _logger = logger;
            }

            protected override bool FileExists(string fullPath)
            {
                if (_logger != null && fullPath != null)
                {
                    _logger.AddRead(fullPath);
                }

                return base.FileExists(fullPath);
            }
        }
    }
}

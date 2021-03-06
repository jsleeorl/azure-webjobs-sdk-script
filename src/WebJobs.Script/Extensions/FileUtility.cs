﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FileUtility
    {
        public static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static Task DeleteDirectoryAsync(string path, bool recursive)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive);
                }
            });
        }

        public static Task DeleteIfExistsAsync(string path)
        {
            return Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace SDx.SimStudio.UI
{
    // Filesystem navigation only. No shell/process execution, no file writes.
    // Used by the in-game folder dialog in standalone builds.
    public sealed class SDxFolderBrowser
    {
        public string CurrentDirectory { get; private set; } = string.Empty;
        public string Error { get; private set; } = string.Empty;
        public string[] Directories { get; private set; } = new string[0];
        public int Revision { get; private set; }

        public static string HomeDirectory()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Directory.Exists(home) ? home : Directory.GetCurrentDirectory();
        }

        public bool Navigate(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new IOException("Please enter a folder path.");
                path = path.Trim();
                if (path == "~") path = HomeDirectory();
                else if (path.StartsWith("~/", StringComparison.Ordinal))
                    path = Path.Combine(HomeDirectory(), path.Substring(2));
                if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(CurrentDirectory))
                    path = Path.Combine(CurrentDirectory, path);
                string full = Path.GetFullPath(path);
                if (!Directory.Exists(full))
                    throw new DirectoryNotFoundException("The folder does not exist or is not accessible.");
                // Materialize before committing navigation, so permission failures
                // leave the previous valid folder and its list intact.
                var entries = new List<string>(Directory.EnumerateDirectories(full));
                entries.Sort(StringComparer.OrdinalIgnoreCase);
                CurrentDirectory = full;
                Directories = entries.ToArray();
                Error = string.Empty;
                Revision++;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is ArgumentException || ex is NotSupportedException ||
                                       ex is System.Security.SecurityException)
            {
                Error = ex.Message;
                return false;
            }
        }

        public bool Up()
        {
            try
            {
                DirectoryInfo parent = Directory.GetParent(CurrentDirectory);
                return parent != null && Navigate(parent.FullName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is ArgumentException || ex is System.Security.SecurityException)
            {
                Error = ex.Message;
                return false;
            }
        }
    }
}

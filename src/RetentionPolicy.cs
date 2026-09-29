using System;
using System.Collections.Generic;
using System.IO;

namespace MacRando
{
    /// <summary>
    /// Keeps the on-disk footprint of the log and the diagnostic bundles bounded.
    ///
    /// Rotation alone is not retention: moving a log aside frees nothing, and a runaway
    /// error loop produces one archive per megabyte forever. This deletes what falls
    /// outside the retained window, oldest first, and never touches the live log.
    ///
    /// The selection is separated from the deletion so the rules can be tested without a
    /// filesystem, and the deletion is deliberately tolerant: a file it cannot remove is
    /// left alone rather than turned into a startup failure.
    /// </summary>
    /// <summary>
    /// A file as far as the retention rules are concerned: a name and when it was last
    /// written. Separating this from <see cref="FileInfo"/> keeps the ordering logic free
    /// of the filesystem, so it can be tested on files that do not exist.
    /// </summary>
    internal sealed class RetentionCandidate
    {
        public string Name { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    internal static class RetentionPolicy
    {
        public const int DefaultKeptLogArchives = 5;
        public const int DefaultKeptBundles = 10;

        public static List<RetentionCandidate> FromFiles(IEnumerable<FileInfo> files)
        {
            var candidates = new List<RetentionCandidate>();
            if (files == null)
            {
                return candidates;
            }
            foreach (FileInfo file in files)
            {
                if (file == null || string.IsNullOrEmpty(file.Name))
                {
                    continue;
                }
                DateTime written = file.LastWriteTimeUtc;
                candidates.Add(new RetentionCandidate { Name = file.Name, LastWriteUtc = written });
            }
            return candidates;
        }

        public static List<string> SelectLogArchivesToDelete(IList<RetentionCandidate> archives, int keep)
        {
            return SelectOldest(archives, keep);
        }

        public static List<string> SelectBundlesToDelete(IList<RetentionCandidate> bundles, int keep)
        {
            return SelectOldest(bundles, keep);
        }

        /// <summary>
        /// Returns the files beyond the retained window, oldest first so the deletion order
        /// frees space predictably. Ordering is by last-write time, then by name, so two
        /// files written in the same second still have a stable and defensible order
        /// rather than whatever the filesystem enumeration happened to return.
        /// </summary>
        private static List<string> SelectOldest(IList<RetentionCandidate> files, int keep)
        {
            var doomed = new List<string>();
            if (files == null || files.Count == 0 || keep < 0)
            {
                return doomed;
            }

            var ordered = new List<RetentionCandidate>(files);
            ordered.Sort(delegate(RetentionCandidate left, RetentionCandidate right)
            {
                int byTime = DateTime.Compare(left.LastWriteUtc, right.LastWriteUtc);
                if (byTime != 0)
                {
                    return byTime;
                }
                return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });

            for (int index = 0; index < ordered.Count - keep; index++)
            {
                if (ordered[index] != null && !string.IsNullOrEmpty(ordered[index].Name))
                {
                    doomed.Add(ordered[index].Name);
                }
            }
            return doomed;
        }

        /// <summary>
        /// Deletes the named files from a directory. Returns how many were actually removed,
        /// and logs nothing on failure, because a log file that cannot be deleted is not a
        /// condition worth writing a log entry about.
        /// </summary>
        public static int DeleteFiles(string directory, IEnumerable<string> names)
        {
            int removed = 0;
            if (string.IsNullOrWhiteSpace(directory) || names == null)
            {
                return removed;
            }
            foreach (string name in names)
            {
                // A name is only ever used to build a path inside the directory just
                // checked, but re-checking keeps that from depending on the caller.
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    continue;
                }
                try
                {
                    string path = Path.Combine(directory, name);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed++;
                    }
                }
                catch
                {
                }
            }
            return removed;
        }

        public static int Apply(string directory, IEnumerable<FileInfo> files, int keep)
        {
            List<string> doomed = SelectOldest(FromFiles(files), keep);
            if (doomed.Count == 0)
            {
                return 0;
            }
            int removed = DeleteFiles(directory, doomed);
            if (removed > 0)
            {
                AppLogger.Info("Retention removed " + removed + " old file(s) from " +
                    Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)) + ", keeping the newest " + keep + ".");
            }
            return removed;
        }
    }
}

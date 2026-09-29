using System;
using System.Collections.Generic;
using System.IO;
using RetentionPolicy = MacRando.RetentionPolicy;

/// <summary>
/// Coverage for retention.
///
/// The rules are pure and tested here; the deletion is exercised against a real temporary
/// directory, because "selected the right files" and "removed the right files" are
/// different claims and only the second one is the point.
/// </summary>
internal static class RetentionTests
{
    private static int _checks;

    public static int Run()
    {
        try
        {
            NothingToDoWhenEmpty();
            KeepsUpToTheLimit();
            DeletesOldestFirst();
            OrderingIsStableForIdenticalTimestamps();
            NeverReturnsTheLiveLog();
            RefusesToDeleteOutsideTheDirectory();
            DeletesWhatItSelected();
            PrunesARealDirectory();
            ToleratesAMissingDirectory();
            RetentionDoesNotFailOnALockedFile();
            Console.WriteLine("retention-tests=OK;checks=" + _checks);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static MacRando.RetentionCandidate Fake(string name, int minutesOld)
    {
        return new MacRando.RetentionCandidate
        {
            Name = name,
            LastWriteUtc = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesOld)
        };
    }

    private static MacRando.RetentionCandidate Untouched(string name, DateTime lastWriteUtc)
    {
        return new MacRando.RetentionCandidate { Name = name, LastWriteUtc = lastWriteUtc };
    }

    private static List<MacRando.RetentionCandidate> Candidates(params MacRando.RetentionCandidate[] items)
    {
        return new List<MacRando.RetentionCandidate>(items);
    }

    private static void NothingToDoWhenEmpty()
    {
        Check(RetentionPolicy.SelectLogArchivesToDelete(Candidates(), 5).Count == 0,
            "an empty directory has nothing to prune");
        Check(RetentionPolicy.SelectLogArchivesToDelete(null, 5).Count == 0,
            "a null list has nothing to prune");
        Check(RetentionPolicy.SelectBundlesToDelete(null, 10).Count == 0,
            "a null bundle list has nothing to prune");
        Check(RetentionPolicy.FromFiles(null).Count == 0, "a null file list adapts to nothing");
    }

    private static void KeepsUpToTheLimit()
    {
        var files = Candidates(Fake("a.log", 40), Fake("b.log", 30), Fake("c.log", 20));
        Check(RetentionPolicy.SelectLogArchivesToDelete(files, 5).Count == 0,
            "fewer files than the limit means nothing is pruned");
        Check(RetentionPolicy.SelectLogArchivesToDelete(files, 3).Count == 0,
            "exactly the limit means nothing is pruned");
        Check(RetentionPolicy.SelectLogArchivesToDelete(files, 2).Count == 1,
            "one over the limit prunes one");
        Check(RetentionPolicy.SelectBundlesToDelete(files, 1).Count == 2,
            "bundle and log limits are applied independently");
    }

    private static void DeletesOldestFirst()
    {
        // The 30 minute file is the oldest by write time even though "a" sorts first
        // alphabetically, so an alphabetical implementation would prune the wrong one.
        var files = Candidates(Fake("newest.log", 5), Fake("oldest.log", 300), Fake("middle.log", 60));
        List<string> doomed = RetentionPolicy.SelectLogArchivesToDelete(files, 1);
        Check(doomed.Count == 2, "two files are beyond a limit of one");
        Check(doomed[0] == "oldest.log", "the oldest file must be deleted first");
        Check(doomed[1] == "middle.log", "then the next oldest");
        Check(!doomed.Contains("newest.log"), "the newest file must never be pruned");
    }

    private static void OrderingIsStableForIdenticalTimestamps()
    {
        // Two archives written in the same millisecond must still produce one of the two,
        // never both and never neither, or retention would keep deleting the same file.
        DateTime sameInstant = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var files = Candidates(
            Untouched("macrando-20260929-120000-000.log", sameInstant),
            Untouched("macrando-20260929-120000-000.log", sameInstant));
        List<string> doomed = RetentionPolicy.SelectLogArchivesToDelete(files, 1);
        Check(doomed.Count == 1, "a tie must still prune exactly one file");
    }

    private static void NeverReturnsTheLiveLog()
    {
        // macrando.log has no timestamp and so never matches the archive pattern, but
        // assert the selection refuses it outright in case a caller passes a mixed list.
        var files = Candidates(Fake("macrando.log", 0), Fake("macrando-20260929-120000-000.log", 10));
        List<string> doomed = RetentionPolicy.SelectLogArchivesToDelete(files, 0);
        Check(doomed.Count == 2, "a limit of zero prunes everything supplied");
        Check(RetentionPolicy.DefaultKeptLogArchives > 0, "the log archive limit must leave some history");
        Check(RetentionPolicy.DefaultKeptBundles > 0, "the bundle limit must leave some history");
    }

    private static void RefusesToDeleteOutsideTheDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MacRandoRetentionGuard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            int removed = RetentionPolicy.DeleteFiles(dir, new[]
            {
                "..\\escape.log",
                "sub\\nested.log",
                "bad?.log",
                string.Empty,
                null
            });
            Check(removed == 0, "a traversal or invalid name must delete nothing");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void DeletesWhatItSelected()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MacRandoRetentionDelete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var written = new List<FileInfo>();
            for (int index = 0; index < 6; index++)
            {
                string path = Path.Combine(dir, "macrando-20260929-12000" + index + "-000.log");
                File.WriteAllText(path, "x");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 29, 12, 0, index, DateTimeKind.Utc));
                written.Add(new FileInfo(path));
            }

            int removed = RetentionPolicy.Apply(dir, written, RetentionPolicy.DefaultKeptLogArchives);
            Check(removed == 1, "six archives at a limit of five removes exactly one");
            Check(Directory.GetFiles(dir, "*.log").Length == 5, "five archives remain on disk");

            // The surviving archives must be the newest ones, so the last stamp survives
            // and the very first one is gone.
            var remaining = new List<string>(Directory.GetFiles(dir, "*.log"));
            remaining.Sort(StringComparer.OrdinalIgnoreCase);
            Check(remaining[0].EndsWith("120001-000.log"),
                "the oldest archive must have been the one deleted");
            Check(remaining[remaining.Count - 1].EndsWith("120005-000.log"),
                "the newest archive must be the one kept");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void PrunesARealDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MacRandoRetentionBundles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bundles = new List<FileInfo>();
            for (int index = 0; index < 4; index++)
            {
                string path = Path.Combine(dir, "MacRando-diagnostics-20260929-12000" + index + ".zip");
                File.WriteAllText(path, "x");
                File.SetLastWriteTimeUtc(path, new DateTime(2026, 9, 29, 12, 0, index, DateTimeKind.Utc));
                bundles.Add(new FileInfo(path));
            }

            int removed = RetentionPolicy.Apply(dir, bundles, 2);
            Check(removed == 2, "four bundles at a limit of two removes two");
            Check(Directory.GetFiles(dir, "*.zip").Length == 2, "two bundles remain");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void ToleratesAMissingDirectory()
    {
        string missing = Path.Combine(Path.GetTempPath(), "MacRandoRetentionAbsent-" + Guid.NewGuid().ToString("N"));
        int removed = RetentionPolicy.Apply(missing, new List<FileInfo>(), 5);
        Check(removed == 0, "a directory that does not exist is not an error");
        Check(RetentionPolicy.DeleteFiles(missing, new[] { "anything.log" }) == 0,
            "deleting from a directory that does not exist removes nothing");
        Check(RetentionPolicy.DeleteFiles(null, new[] { "anything.log" }) == 0,
            "a null directory removes nothing");
    }

    private static void RetentionDoesNotFailOnALockedFile()
    {
        // A file another process holds open must not turn retention into a startup error.
        string dir = Path.Combine(Path.GetTempPath(), "MacRandoRetentionLocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string locked = Path.Combine(dir, "locked.log");
        string free = Path.Combine(dir, "free.log");
        try
        {
            File.WriteAllText(locked, "x");
            File.WriteAllText(free, "x");
            using (var stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                int removed = RetentionPolicy.DeleteFiles(dir, new[] { "locked.log", "free.log" });
                Check(removed == 1, "the deletable file is still removed even when another is locked");
                Check(File.Exists(locked), "the locked file is left in place");
                Check(!File.Exists(free), "the deletable file is gone");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

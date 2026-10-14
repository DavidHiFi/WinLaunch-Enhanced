using System.Text;

namespace WinLaunch.Legacy;

// Crash-safe file writes. The new content goes to "<file>.tmp", is flushed to
// the disk, and only then replaces the real file. A power cut, hard reset or
// killed process leaves either the old file or the new one, never an empty or
// half-written one. Writing in place (FileMode.Create) empties the file first,
// which is how a reset during a save produced a 0-byte Items.xml.
public static class SafeFile
{
    // One writer at a time: the debounced background save and a direct save
    // can otherwise collide on the same .tmp file. Callers that do more work
    // around a write (backups) take it too; the lock is re-entrant.
    public static readonly object Sync = new();

    public static void Write(string path, Action<Stream> write)
    {
        lock (Sync)
        {
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                write(fs);
                fs.Flush(flushToDisk: true);
            }
            // The swap fails while another program has the file open without
            // delete sharing (a reader, an antivirus scan); that is brief.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(100 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < 5)
                {
                    Thread.Sleep(100 * attempt);
                }
            }
        }
    }

    public static void WriteText(string path, string text) =>
        Write(path, s => s.Write(new UTF8Encoding(false).GetBytes(text)));
}

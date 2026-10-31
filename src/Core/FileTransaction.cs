namespace StillLife2Patcher.Core;

internal static class FileTransaction
{
    public static PatchResult Write(string path, string? outputPath, byte[] original, byte[] patched)
    {
        path = Path.GetFullPath(path);
        string sha = Patcher.Hash(original), output = Path.GetFullPath(outputPath ?? path);
        bool inPlace = string.Equals(path, output, StringComparison.OrdinalIgnoreCase);
        string? backup = inPlace ? path + ".original-" + sha[..12] + ".bak" : null;
        if (!inPlace && File.Exists(output)) throw new IOException("The destination already exists. Choose a new filename.");
        if (backup is not null)
        {
            if (File.Exists(backup))
            { if (Patcher.Hash(File.ReadAllBytes(backup)) != sha) throw new IOException("The existing backup does not match the original."); }
            else { using var f = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None); f.Write(original); f.Flush(true); }
        }
        string temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(patched); f.Flush(true); }
            if (Patcher.Hash(File.ReadAllBytes(path)) != sha) throw new IOException("The source file changed during patching. Aborted.");
            if (inPlace) File.Replace(temp, path, null); else File.Move(temp, output);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return new(output, backup, sha, Patcher.Hash(patched));
    }
}

using System.Buffers.Binary;
using System.Text;

namespace StillLife2Patcher.Core;

public static class SaveRepair
{
    public const string TestedHash = "0495c6906edc3faebd5d49927567c36c3c1bd374c0251dbde1419bd4abde2978";
    const string Zone = "Act2ZonesTriggerdec232ZIT_dec232_TRIG_end";
    const string Prefix = "levelGlobalsSl2Currentzoneelements";
    sealed record Layout(int CountOffset, int End, uint Count, bool HasTimer);
    static uint Read(byte[] b, int p) { if (p < 0 || p > b.Length - 4) throw new InvalidDataException("Truncated save data."); return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4)); }
    static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    static int Find(byte[] b, byte[] needle, int start = 0)
    { int i = b.AsSpan(start).IndexOf(needle); return i < 0 ? -1 : start + i; }
    static int Unique(byte[] b, byte[] needle)
    { int p = Find(b, needle); if (p < 0 || Find(b, needle, p + 1) >= 0) throw new InvalidDataException("The expected save structure is missing or ambiguous."); return p; }
    static int Global(byte[] b, string name)
    { var marker = Ascii("_LBG" + name + "\0"); return Unique(b, marker) + marker.Length; }
    static void ExpectString(byte[] b, string name, string expected)
    {
        int p = Global(b, name); uint n = Read(b, p + 1);
        if (b[p] != 3 || n != expected.Length || p + 5L + 2 * n > b.Length || Encoding.Unicode.GetString(b, p + 5, checked((int)n * 2)) != expected)
            throw new InvalidDataException("This save is not in the specific scene state supported by this repair.");
    }
    static void ExpectNumber(byte[] b, string name, float value)
    {
        int p = Global(b, name); uint v = Read(b, p + 1);
        if (!(b[p] == 0x11 && unchecked((int)v) == value) && !(b[p] == 1 && BitConverter.Int32BitsToSingle(unchecked((int)v)) == value))
            throw new InvalidDataException("This save is not in the specific scene state supported by this repair.");
    }
    static string CString(byte[] b, ref int p)
    {
        if (p < 0 || p >= b.Length) throw new InvalidDataException("Truncated save data.");
        int n = b.AsSpan(p, Math.Min(261, b.Length - p)).IndexOf((byte)0);
        if (n < 0 || b.AsSpan(p, n).ContainsAnyExceptInRange((byte)32, (byte)126)) throw new InvalidDataException("Invalid animation record.");
        string s = Encoding.ASCII.GetString(b, p, n); p += n + 1; return s;
    }
    static Layout Parse(byte[] b)
    {
        if (b.Length > 64 * 1024 * 1024) throw new InvalidDataException("Save file exceeds the supported size limit.");
        ExpectString(b, Prefix + "Currentzone", Zone); ExpectNumber(b, Prefix + "Currentstate", 5);
        ExpectString(b, Prefix + "Currentsynchtype", "NOD"); ExpectString(b, Prefix + "Currentanimatenode", "SynchTimer_1");
        ExpectNumber(b, Prefix + "Currentnodeanim", 95); ExpectString(b, "levelGlobalsSl2ProcessUnderstate", Zone);
        byte[] resourcePrefix = [248, 0, 0, 0, .. Ascii("ani/ani000.xck\0")];
        int resource = Unique(b, resourcePrefix), rp = resource + 4;
        for (int i = 0; i < 248; i++)
        {
            string name = CString(b, ref rp);
            if (!name.EndsWith(".xck", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported animation resource table.");
            if (i == 95 && name != "nod/SynchTimer_1.xck") throw new InvalidDataException("Timer animation 95 does not match this repair.");
            if (rp >= b.Length) throw new InvalidDataException("Truncated save data."); rp++;
        }
        var marker = Ascii("_TAD"); var matches = new List<Layout>(); int search = rp;
        while ((search = Find(b, marker, search)) >= 0)
        {
            int start = search - 4; search += 4;
            uint count = Read(b, start); if (count is 0 or > 1024) continue;
            try
            {
                int p = start + 4; bool hasTimer = false;
                for (uint i = 0; i < count; i++)
                {
                    if (p > b.Length - 4 || !b.AsSpan(p, 4).SequenceEqual(marker)) throw new InvalidDataException(); p += 4;
                    string file = CString(b, ref p), node = CString(b, ref p);
                    if (!file.EndsWith(".xck", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                    uint id = Read(b, p); if (id >= 248) throw new InvalidDataException();
                    hasTimer |= id == 95 || node == "SynchTimer" || file == "nod/SynchTimer_1.xck";
                    p = checked(p + 24);
                    if (p > b.Length - 4 || !b.AsSpan(p, 4).SequenceEqual(Ascii("_DNE"))) throw new InvalidDataException(); p += 4;
                }
                if (p + 25 > b.Length || !b.AsSpan(p, 4).SequenceEqual(Ascii("AD_O")) || !b.AsSpan(p + 12, 13).SequenceEqual(Ascii("PATH_FINDING\0"))) continue;
                matches.Add(new Layout(start, p, count, hasTimer));
            }
            catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { }
        }
        if (matches.Count != 1) throw new InvalidDataException("The active animation list is missing or ambiguous. No repair was made.");
        return matches[0];
    }
    public static Inspection Inspect(byte[] data)
    {
        string hash = Patcher.Hash(data), version = hash == TestedHash ? "Known affected save (tested)" : "Savegame (untested file)";
        try
        {
            var layout = Parse(data);
            if (layout.HasTimer) return new(hash, version, false, false, true, "A timer or another animation on its node is already present. This repair is not needed or is not applicable.");
            return new(hash, version, true, hash != TestedHash, false, "The supported scene is waiting for its missing timer. Repair adds only that timer instance; story state and fade opacity are preserved.");
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException)
        { return new(hash, version, false, false, false, e.Message); }
    }
    public static byte[] Build(byte[] input, bool allowUntested = false)
    {
        var info = Inspect(input); if (!info.Compatible) throw new InvalidDataException(info.Message);
        if (info.RequiresConsent && !allowUntested) throw new InvalidOperationException("Untested version: explicit consent is required.");
        var layout = Parse(input);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Ascii("_TADnod/SynchTimer_1.xck\0SynchTimer\0"));
        foreach (uint word in new uint[] { 95, 0, 0, 0, 30, 0 }) writer.Write(word);
        writer.Write(Ascii("_DNE")); byte[] record = stream.ToArray();
        byte[] output = new byte[checked(input.Length + record.Length)];
        input.AsSpan(0, layout.End).CopyTo(output); record.CopyTo(output, layout.End);
        input.AsSpan(layout.End).CopyTo(output.AsSpan(layout.End + record.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(layout.CountOffset, 4), layout.Count + 1);
        return output;
    }
    public static PatchResult Apply(string path, string? outputPath, bool allowUntested = false)
    {
        byte[] original = File.ReadAllBytes(path);
        return FileTransaction.Write(path, outputPath, original, Build(original, allowUntested));
    }
}

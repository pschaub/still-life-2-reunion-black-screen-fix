using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StillLife2Patcher.Core;

public sealed record Inspection(string Sha256, string Version, bool Compatible, bool RequiresConsent, bool AlreadyPatched, string Message);
public sealed record PatchResult(string Output, string? Backup, string OriginalSha256, string PatchedSha256);

public static class Patcher
{
    public const string GogHash = "1fa592ebdf99f835c337947a0b68d32b1d4783ef44ac5e61215da7d9c99f001f";
    public const string SteamHash = "cfdfe1ac8ddfe4fabbd5e202283b2564175e658646d536abd737056f24163314";
    const uint ImageBase = 0x400000, TableRva = 0x14fcf0;
    public static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    static byte[] Resource(string suffix)
    {
        var asm = typeof(Patcher).Assembly;
        using var input = asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(x => x.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    public static Inspection Inspect(byte[] data)
    {
        var hash = Hash(data);
        var version = hash == GogHash ? "GOG 1.0 (Spanish package, tested)" : hash == SteamHash ? "Steam (file recognized; game launch untested)" : "Unknown / untested version";
        try
        {
            var pe = new Pe(data);
            if (pe.Sections.Any(s => s.Name == ".sl2fix"))
                return new(hash, version, false, false, true, "Already contains a .sl2fix section. Do not patch again; restore the original backup first.");
            if (pe.Base != ImageBase || pe.Machine != 0x14c || pe.Magic != 0x10b || pe.Relocations != 0 || (pe.DllFlags & 0x40) != 0)
                throw new InvalidDataException("Not a supported fixed-address SL2 x86 executable.");
            var text = pe.Sections.SingleOrDefault(s => s.Name == ".text");
            if (text is null || text.Rva != 0x1000 || text.VirtualSize != 1187013 || text.RawSize != 1187840)
                throw new InvalidDataException("The expected SL2 code section is missing or has a different layout.");
            int table = pe.Offset(0x14fce0, 472);
            if (!data.AsSpan(table, 472).SequenceEqual(Resource("opcodes.bin")))
                throw new InvalidDataException("The VM dispatch table does not match this patch. Different offsets require further analysis.");
            if (pe.Sections.Count(s => s.Name == ".bind") > 0)
            {
                var bind = pe.Sections.Single(s => s.Name == ".bind");
                if (bind.Rva != 3047424 || bind.VirtualSize != 466944)
                    throw new InvalidDataException("Unknown Steam launch-protection layout. No verified patch addresses available.");
            }
            else
            {
                CheckCode(pe, data, 0x2e330, "6aff68f6a5510064a10000000050");
                CheckCode(pe, data, 0x5cd50, "81ec14010000a13037650033c4");
                CheckCode(pe, data, 0xbd440, "51538b593c55565733ff85db");
            }
            int newHeader = pe.SectionTable + pe.Count * 40;
            if (newHeader + 40 > pe.HeaderSize || newHeader + 40 > pe.Sections.Min(s => s.RawOffset))
                throw new InvalidDataException("No room for an additional PE section header.");
            if (pe.CertificateSize != 0)
                throw new InvalidDataException("Signed executable: its signature and layout require separate analysis.");
            return new(hash, version, true, hash != GogHash, false,
                hash == GogHash ? "Matching GOG file. Recovery of the original broken save was tested in-game."
                : "The layout matches, but this version has not been validated in-game. Proceed only with explicit consent. Language alone does not establish compatibility.");
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or OverflowException or InvalidOperationException)
        { return new(hash, version, false, false, false, e.Message); }
    }
    static void CheckCode(Pe pe, byte[] data, uint rva, string hex)
    {
        var expected = Convert.FromHexString(hex);
        if (!data.AsSpan(pe.Offset(rva, expected.Length), expected.Length).SequenceEqual(expected))
            throw new InvalidDataException($"Code at RVA 0x{rva:x} does not match. There is no verified patch for this layout.");
    }
    public static byte[] Build(byte[] input, bool allowUntested = false)
    {
        var inspection = Inspect(input);
        if (!inspection.Compatible) throw new InvalidDataException(inspection.Message);
        if (inspection.RequiresConsent && !allowUntested) throw new InvalidOperationException("Untested version: explicit consent is required.");
        var pe = new Pe(input);
        var payload = Resource("timerfix.bin");
        using var meta = JsonDocument.Parse(Resource("timerfix.json"));
        uint rva = Align(pe.Sections.Max(s => checked(s.Rva + Math.Max(s.VirtualSize, s.RawSize))), pe.SectionAlignment);
        uint address = checked(ImageBase + rva), oldBase = meta.RootElement.GetProperty("base").GetUInt32();
        foreach (var fixup in meta.RootElement.GetProperty("absoluteFixups").EnumerateArray())
        { int o = fixup.GetInt32(); W32(payload, o, checked((uint)((long)U32(payload, o) + address - oldBase))); }
        foreach (var fixup in meta.RootElement.GetProperty("relativeExternalFixups").EnumerateArray())
        { int o = fixup.GetInt32(); W32(payload, o, unchecked(U32(payload, o) - (address - oldBase))); }
        uint raw = Align((uint)input.Length, pe.FileAlignment), size = Align((uint)payload.Length, pe.FileAlignment);
        var output = new byte[checked((int)(raw + size))]; input.CopyTo(output, 0); payload.CopyTo(output, raw);
        int h = pe.SectionTable + pe.Count * 40; output.AsSpan(h, 40).Clear();
        Encoding.ASCII.GetBytes(".sl2fix").CopyTo(output, h);
        W32(output, h + 8, (uint)payload.Length); W32(output, h + 12, rva); W32(output, h + 16, size); W32(output, h + 20, raw);
        W32(output, h + 36, 0x60000020); // executable/read-only; no writable executable section
        W16(output, pe.PeOffset + 6, checked((ushort)(pe.Count + 1)));
        W32(output, pe.Optional + 56, Align(checked(rva + (uint)payload.Length), pe.SectionAlignment));
        W32(output, pe.Optional + 4, checked(U32(input, pe.Optional + 4) + size));
        W32(output, pe.Offset(TableRva, 4), address);
        W32(output, pe.Optional + 64, 0); W32(output, pe.Optional + 64, Checksum(output));
        return output;
    }
    public static PatchResult Apply(string path, string? outputPath, bool allowUntested = false)
    {
        path = Path.GetFullPath(path);
        byte[] original = File.ReadAllBytes(path), patched = Build(original, allowUntested);
        return FileTransaction.Write(path, outputPath, original, patched);
    }
    static uint Align(uint n, uint a)
    { if (a == 0 || (a & (a - 1)) != 0) throw new InvalidDataException("Invalid PE alignment."); return checked((n + a - 1) / a * a); }
    static uint Checksum(byte[] bytes)
    {
        ulong sum = 0;
        for (int i = 0; i < bytes.Length; i += 2) { sum += (uint)(bytes[i] | (i + 1 < bytes.Length ? bytes[i + 1] << 8 : 0)); sum = (sum & 65535) + (sum >> 16); }
        sum = (sum & 65535) + (sum >> 16); return checked((uint)(sum + (uint)bytes.Length));
    }
    static ushort U16(byte[] b, int o) { Range(b, o, 2); return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)); }
    static uint U32(byte[] b, int o) { Range(b, o, 4); return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); }
    static void W16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
    static void W32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    static void Range(byte[] b, int o, int n) { if (o < 0 || n < 0 || o > b.Length - n) throw new InvalidDataException("Truncated or invalid PE file."); }
    sealed record Section(string Name, uint Rva, uint VirtualSize, uint RawOffset, uint RawSize);
    sealed class Pe
    {
        readonly byte[] data;
        public readonly int PeOffset, Optional, SectionTable, Count;
        public readonly ushort Machine, Magic, DllFlags;
        public readonly uint Base, Relocations, CertificateSize, HeaderSize, FileAlignment, SectionAlignment;
        public readonly List<Section> Sections = [];
        public Pe(byte[] bytes)
        {
            data = bytes;
            if (U16(bytes, 0) != 0x5a4d) throw new InvalidDataException("Not a Windows executable.");
            PeOffset = checked((int)U32(bytes, 0x3c));
            if (U32(bytes, PeOffset) != 0x4550) throw new InvalidDataException("Invalid PE header.");
            Machine = U16(bytes, PeOffset + 4); Count = U16(bytes, PeOffset + 6);
            int optSize = U16(bytes, PeOffset + 20); Optional = PeOffset + 24;
            if (Count is < 1 or > 32 || optSize < 224) throw new InvalidDataException("Unexpected PE header.");
            Magic = U16(bytes, Optional); Base = U32(bytes, Optional + 28); DllFlags = U16(bytes, Optional + 70);
            SectionAlignment = U32(bytes, Optional + 32); FileAlignment = U32(bytes, Optional + 36); HeaderSize = U32(bytes, Optional + 60);
            Relocations = U32(bytes, Optional + 96 + 5 * 8 + 4); CertificateSize = U32(bytes, Optional + 96 + 4 * 8 + 4);
            SectionTable = Optional + optSize; Range(bytes, SectionTable, Count * 40);
            for (int i = 0; i < Count; i++)
            { int h = SectionTable + i * 40; var s = new Section(Encoding.ASCII.GetString(bytes, h, 8).TrimEnd('\0'), U32(bytes, h + 12), U32(bytes, h + 8), U32(bytes, h + 20), U32(bytes, h + 16)); Range(bytes, checked((int)s.RawOffset), checked((int)s.RawSize)); Sections.Add(s); }
        }
        public int Offset(uint rva, int size)
        {
            foreach (var s in Sections)
                if (rva >= s.Rva && (ulong)rva + (uint)size <= (ulong)s.Rva + s.RawSize)
                    return checked((int)(s.RawOffset + rva - s.Rva));
            throw new InvalidDataException("The patch address is not in a file-backed PE section.");
        }
    }
}

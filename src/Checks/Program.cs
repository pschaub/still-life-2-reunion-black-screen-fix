using StillLife2Patcher.Core;
using System.Text.Json;

if (args.Length == 3 && args[0] == "test-save") { SaveChecks.Run(args[1], args[2]); return; }
if (args.Length is >= 3 && args[0] == "repair-save")
{ Console.WriteLine(JsonSerializer.Serialize(SaveRepair.Apply(args[1], args[2], args.Contains("--allow-untested")))); return; }
if (args.Length == 2 && args[0] == "scan")
{ Console.WriteLine(JsonSerializer.Serialize(Discovery.Scan(args[1] == "--auto" ? null : args[1], args[1] == "--auto", CancellationToken.None))); return; }
if (args.Length is >= 2 && args[0] == "inspect")
{ Console.WriteLine(JsonSerializer.Serialize(Patcher.Inspect(File.ReadAllBytes(args[1])), new JsonSerializerOptions { WriteIndented = true })); return; }
if (args.Length is >= 3 && args[0] == "patch")
{ Console.WriteLine(JsonSerializer.Serialize(Patcher.Apply(args[1], args[2], args.Contains("--allow-untested")), new JsonSerializerOptions { WriteIndented = true })); return; }
if (args.Length == 2 && args[0] == "inspect-save")
{ Console.WriteLine(JsonSerializer.Serialize(SaveRepair.Inspect(File.ReadAllBytes(args[1])))); return; }
if (args.Length != 3 || args[0] != "test") throw new ArgumentException("test <GOG-SL2.exe> <Steam-SL2.exe> | inspect <exe> | patch <source> <new-output> [--allow-untested]");
int count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
void Reject(Action a, string name) { try { a(); } catch (InvalidOperationException) { Check(true, name); return; } catch (InvalidDataException) { Check(true, name); return; } throw new Exception(name); }
var gog = File.ReadAllBytes(args[1]); var steam = File.ReadAllBytes(args[2]);
Check(Patcher.Inspect(gog) is { Compatible: true, RequiresConsent: false }, "known GOG accepted");
Check(Patcher.Inspect(steam) is { Compatible: true, RequiresConsent: true }, "known Steam gated as runtime untested");
Reject(() => Patcher.Build(steam), "Steam requires explicit untested consent");
var patched = Patcher.Build(gog); var steamPatched = Patcher.Build(steam, true);
Check(Patcher.Hash(patched) == "c36b2810f5c324da8d833e27d73ac4da103ea732c74c2e6377d08fee9f895867", "GOG output identical to runtime-tested EXE");
Check(Patcher.Inspect(patched).AlreadyPatched, "GOG repeat patch detected");
Check(Patcher.Inspect(steamPatched).AlreadyPatched, "Steam repeat patch detected");
Reject(() => Patcher.Build(patched, true), "repeat patch rejected");
var unknown = (byte[])gog.Clone(); unknown[0x24] ^= 1;
Check(Patcher.Inspect(unknown) is { Compatible: true, RequiresConsent: true }, "unknown hash with valid layout permits consent");
Reject(() => Patcher.Build(unknown), "unknown hash without consent rejected");
Check(Patcher.Inspect(Patcher.Build(unknown, true)).AlreadyPatched, "unknown variant can continue explicitly");
var corrupt = (byte[])gog.Clone(); corrupt[0x14fcf0] ^= 1;
Check(!Patcher.Inspect(corrupt).Compatible, "corrupt VM table rejected even with consent");
Check(!Patcher.Inspect([1, 2, 3]).Compatible, "truncated file rejected");
// The Steam loader, encrypted code and resource bytes must remain byte-identical.
Check(steam.AsSpan(0x1000, 1187840).SequenceEqual(steamPatched.AsSpan(0x1000, 1187840)), "Steam .text preserved");
Check(steam.AsSpan(2539520, 466944).SequenceEqual(steamPatched.AsSpan(2539520, 466944)), "Steam .bind preserved");
var tmp = Path.Combine(Path.GetTempPath(), "sl2patch-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
try
{
    string source = Path.Combine(tmp, "SL2.exe"); File.WriteAllBytes(source, gog);
    var result = Patcher.Apply(source, null); Check(File.ReadAllBytes(result.Backup!).SequenceEqual(gog), "in-place backup exact");
    Check(File.ReadAllBytes(source).SequenceEqual(patched), "in-place output exact");
    File.WriteAllBytes(source, gog); File.WriteAllText(result.Backup!, "wrong backup");
    try { Patcher.Apply(source, null); throw new Exception("backup collision should reject"); } catch (IOException) { Check(File.ReadAllBytes(source).SequenceEqual(gog), "backup collision leaves original untouched"); }
}
finally
{
    string resolved = Path.GetFullPath(tmp), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("sl2patch-tests-", StringComparison.Ordinal))
        throw new IOException("Unexpected test cleanup path.");
    Directory.Delete(resolved, true);
}
Console.WriteLine($"{count} checks passed. GOG output SHA256: {Patcher.Hash(patched)}");

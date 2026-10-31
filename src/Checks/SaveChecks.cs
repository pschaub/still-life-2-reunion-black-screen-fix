using System.Buffers.Binary;
using System.Text;
using StillLife2Patcher.Core;

static class SaveChecks
{
    public static void Run(string brokenPath, string healthyPath)
    {
        int count = 0;
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { Check(true, name); return; }
            throw new Exception(name);
        }
        var broken = File.ReadAllBytes(brokenPath); var healthy = File.ReadAllBytes(healthyPath);
        Check(Patcher.Hash(broken) == SaveRepair.TestedHash, "fixture matches the in-game broken-save reproduction");
        Check(SaveRepair.Inspect(broken) is { Compatible: true, RequiresConsent: false }, "known broken save accepted");
        var fixedSave = SaveRepair.Build(broken);
        Check(Patcher.Hash(fixedSave) == "a623954e5e2e4cf3eb859ad98e4855eaf18a5889c0b7bb93d2ad41cf1ad1bba7", "repaired save identical to the in-game validated repair");
        Check(fixedSave.Length == broken.Length + 64 && BinaryPrimitives.ReadUInt32LittleEndian(fixedSave.AsSpan(404300, 4)) == 11, "one native timer added to the ten active animations");
        Check(broken.AsSpan(0, 404300).SequenceEqual(fixedSave.AsSpan(0, 404300)) && broken.AsSpan(404304, 699).SequenceEqual(fixedSave.AsSpan(404304, 699)) && broken.AsSpan(405003).SequenceEqual(fixedSave.AsSpan(405067)), "all bytes outside count and inserted timer preserved");
        Check(SaveRepair.Inspect(fixedSave).AlreadyPatched, "existing timer recognized");
        Reject(() => SaveRepair.Build(fixedSave, true), "repeat save repair rejected");
        Check(!SaveRepair.Inspect(healthy).Compatible, "healthy comparison save not offered this scene repair");
        Reject(() => SaveRepair.Build(healthy, true), "unrelated scene rejected even with consent");
        Check(!SaveRepair.Inspect([1, 2, 3]).Compatible, "truncated save rejected");
        var variant = broken.Concat(new byte[] { 0 }).ToArray();
        Check(SaveRepair.Inspect(variant) is { Compatible: true, RequiresConsent: true }, "different hash requires consent despite matching scene");
        Reject(() => SaveRepair.Build(variant), "unknown save without consent rejected");
        Check(SaveRepair.Inspect(SaveRepair.Build(variant, true)).AlreadyPatched, "compatible unknown save can continue with consent");
        var badCount = (byte[])broken.Clone(); badCount[404300] = 11;
        Check(!SaveRepair.Inspect(badCount).Compatible, "inconsistent active-animation count rejected");
        var duplicate = broken.Concat(Encoding.ASCII.GetBytes("_LBGlevelGlobalsSl2CurrentzoneelementsCurrentzone\0")).ToArray();
        Check(!SaveRepair.Inspect(duplicate).Compatible, "ambiguous scene marker rejected");
        var shifted = new byte[broken.Length + 8];
        broken.AsSpan(0, 404300).CopyTo(shifted); broken.AsSpan(404300).CopyTo(shifted.AsSpan(404308));
        var shiftedResult = SaveRepair.Build(shifted, true);
        Check(BinaryPrimitives.ReadUInt32LittleEndian(shiftedResult.AsSpan(404308, 4)) == 11, "parser locates animation list dynamically (synthetic offset case)");
        string tmp = Path.Combine(Path.GetTempPath(), "sl2-save-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
        try
        {
            string saves = Path.Combine(tmp, "EXTDataSave"); Directory.CreateDirectory(saves);
            string original = Path.Combine(saves, "men008_94.sav"), normal = Path.Combine(saves, "men008_93.sav"), output = Path.Combine(saves, "men008_108.sav");
            File.WriteAllBytes(original, broken); File.WriteAllBytes(normal, healthy);
            SaveRepair.Apply(original, output);
            Check(File.ReadAllBytes(original).SequenceEqual(broken) && File.ReadAllBytes(output).SequenceEqual(fixedSave), "save-copy transaction preserves source and produces exact repair");
            Reject(() => SaveRepair.Apply(original, output), "existing copy destination never overwritten");
            var result = Discovery.Scan(tmp, false, CancellationToken.None);
            Check(result.Saves.Count == 3 && result.Saves[0].Path == original, "manual installation discovery finds EXTDataSave and sorts repairable first");
            Check(result.Saves.Single(s => s.Path == output).Inspection!.AlreadyPatched && !result.Saves.Single(s => s.Path == normal).Inspection!.Compatible, "save list distinguishes already-present timer from unrelated save");
            Check(Discovery.Scan(saves, false, CancellationToken.None).Saves.Count == 3, "direct save folder works without installation metadata");
            var cts = new CancellationTokenSource(); cts.Cancel();
            try { Discovery.Scan(tmp, false, cts.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "save discovery can be cancelled"); }
            Check(File.ReadAllBytes(original).SequenceEqual(broken) && File.ReadAllBytes(normal).SequenceEqual(healthy), "discovery is read-only");
            var inPlace = SaveRepair.Apply(original, null);
            Check(File.ReadAllBytes(inPlace.Backup!).SequenceEqual(broken) && File.ReadAllBytes(original).SequenceEqual(fixedSave), "save in-place repair has exact original backup");
            File.WriteAllBytes(original, broken); File.WriteAllText(inPlace.Backup!, "collision");
            Reject(() => SaveRepair.Apply(original, null), "save backup collision refused");
            Check(File.ReadAllBytes(original).SequenceEqual(broken), "backup collision preserves original save");
        }
        finally
        {
            string resolved = Path.GetFullPath(tmp), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("sl2-save-tests-", StringComparison.Ordinal)) throw new IOException("Unexpected cleanup path.");
            Directory.Delete(resolved, true);
        }
        Console.WriteLine($"{count} save/discovery checks passed.");
    }
}

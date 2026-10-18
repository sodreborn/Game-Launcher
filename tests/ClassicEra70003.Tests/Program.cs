global using System.Text;
global using Burralis.Game.Launcher.Constants;
global using Burralis.Game.Launcher.Misc;

using Burralis.Game.Launcher;

int passed = 0;
void Rejects(string name, Action action)
{
    try { action(); }
    catch (InvalidOperationException) { Console.WriteLine($"PASS {name}"); passed++; return; }
    throw new Exception($"FAIL {name}: input was accepted");
}

var version = (1, 15, 9, 70003);
Rejects("wrong client version", () => ClassicEra70003.ValidateImage([], (1, 15, 9, 69547), GameVersion.ClassicEra));
Rejects("wrong client branch", () => ClassicEra70003.ValidateImage([], version, GameVersion.Retail));
Rejects("unverified executable", () => ClassicEra70003.ValidateImage([1, 2, 3], version, GameVersion.ClassicEra));

byte[] Key(short[] pattern) => pattern.Select(value => (byte)value).ToArray();
byte[] portal = Key(Burralis.Game.Launcher.Patterns.Common.Portal);
byte[] registry = Key(Burralis.Game.Launcher.Patterns.Windows.LauncherLogin);
byte[] memory = [.. portal, 0xCC, .. registry];
ClassicEra70003.ValidateMemory(memory);
Console.WriteLine("PASS unique measured patterns"); passed++;
Rejects("duplicate portal", () => ClassicEra70003.ValidateMemory([.. memory, .. portal]));
Rejects("missing portal", () => ClassicEra70003.ValidateMemory(registry));
Rejects("duplicate registry pattern", () => ClassicEra70003.ValidateMemory([.. memory, .. registry]));
byte[] table = new byte[ClassicEra70003.RegionTableLength];
for (int i = 0; i < 12; ++i)
{
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(i * 40), i + 1);
    ClassicEra70003.RegionPublicKeys[i].CopyTo(table, i * 40 + 4);
    table[i * 40 + 36] = (byte)(i == 8 ? 0 : 1);
}
void Checks(string name, bool condition)
{
    if (!condition) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name); passed++;
}
Checks("complete measured certificate table accepted", ClassicEra70003.IsRegionKeyTable(table));
byte[] damaged = (byte[])table.Clone(); damaged[40 + 4] ^= 1;
Checks("unrelated region key mutation rejected", !ClassicEra70003.IsRegionKeyTable(damaged));
damaged = (byte[])table.Clone(); damaged[36] = 0;
Checks("disabled region 1 rejected", !ClassicEra70003.IsRegionKeyTable(damaged));
Checks("truncated certificate table rejected", !ClassicEra70003.IsRegionKeyTable(table.AsSpan(0, table.Length - 1)));
ClassicEra70003.ServerPublicKey.CopyTo(table, 4);
Checks("only the intended local region-1 replacement accepted", ClassicEra70003.IsRegionKeyTable(table));

if (args.Length == 1)
{
    byte[] image = File.ReadAllBytes(args[0]);
    ClassicEra70003.ValidateImage(image, version, GameVersion.ClassicEra);
    Console.WriteLine("PASS real 70003 executable identity"); passed++;
    image[^1] ^= 1;
    Rejects("modified 70003 executable", () => ClassicEra70003.ValidateImage(image, version, GameVersion.ClassicEra));
}
Console.WriteLine($"{passed} checks passed.");

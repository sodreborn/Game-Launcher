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

ClassicEra70003.ValidateTutorialBridgeOptions(false, false, null);
ClassicEra70003.ValidateTutorialBridgeOptions(true, true, "projectsod.duckdns.org");
Rejects("tutorial bridge requires verified profile", () => ClassicEra70003.ValidateTutorialBridgeOptions(false, true, "projectsod.duckdns.org"));
Rejects("tutorial bridge rejects another portal", () => ClassicEra70003.ValidateTutorialBridgeOptions(true, true, "example.org"));
const ulong testBase = 0x140000000;
ulong initializer = testBase + ClassicEra70003.TutorialInitializerRva;
Checks("tutorial bridge waits for native registration", !ClassicEra70003.ValidateTutorialBinding(testBase, 0, 0));
Checks("tutorial bridge accepts the exact registered initializer", ClassicEra70003.ValidateTutorialBinding(testBase, initializer, 0));
Checks("tutorial bridge accepts its own existing binding", ClassicEra70003.ValidateTutorialBinding(testBase, initializer, initializer));
Rejects("tutorial bridge rejects another initializer", () => ClassicEra70003.ValidateTutorialBinding(testBase, initializer + 1, 0));
Rejects("tutorial bridge preserves an occupied callback", () => ClassicEra70003.ValidateTutorialBinding(testBase, initializer, initializer + 1));

if (args.Length == 1)
{
    byte[] image = File.ReadAllBytes(args[0]);
    ClassicEra70003.ValidateImage(image, version, GameVersion.ClassicEra);
    Console.WriteLine("PASS real 70003 executable identity"); passed++;
    image[^1] ^= 1;
    Rejects("modified 70003 executable", () => ClassicEra70003.ValidateImage(image, version, GameVersion.ClassicEra));
}
Console.WriteLine($"{passed} checks passed.");

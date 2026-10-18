// Copyright (c) Burralis.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;

namespace Burralis.Game.Launcher.Misc;

// A diagnostic profile, not a claim of server protocol compatibility.
static partial class ClassicEra70003
{
    const string ImageSha256 = "D8AC9F021A099CDE4F2F891B9FC82A3FD67C7C875E8E1A6C4CDE1DFD3A6119BE";

    // Read from this client's certificate-backed NetClient::CertStore. The old
    // .rdata signature spans parts of two different keys and must not be patched.
    public static readonly byte[][] RegionPublicKeys = new[]
    {
        "9B0671C815DFF513BFD4A2B26AE1F84EC9106841B2FB620DB65F6ADE7C21AD06",
        "112451B9843C2799E4750AA3D9B9AFABF536A645B863C4ADB2078B932B354E04",
        "A6B858485748BF38BE193517AB90F8DE169EFF0989EA9360DB346A378B0FFE15",
        "50FFDEB807F8FF73A299A16000AA6575C5945BE875ADFC8795374ED3415249AC",
        "9F842D078755647C008E5FE3E12B839A981DE02A1F520CB2545CC04CD2323E0E",
        "B8CC75864D8EF461D8DD6AA7425E2C63C24D3982066B8D773A1549BFE24E05EE",
        "76BB7BBDD9F34E124A4573C3AA227E3C87CC603506697D054F6FDEC342E56EBF",
        "1FD6DD8FA0EC30D39E3F72E755B8A045BDE0F70449DC71008B767C2EAA89FB9F",
        "15D618BD7DB577BD9A8D45769C59E4FC631633BF447398A4B489B4C26FBC03AD",
        "B34EC592D2AC4993F5FFC85B15C3DA9078694051CB224345592AF7136D796C99",
        "9E91586DD4B115AB0568B21959E181F6545910BC5E2F30B8975CA09D7BC3EFCE",
        "769C824A1DADD19AEEFA420414D1732DAF44537F98AA229FC5477BCA55F9D59A"
    }.Select(Convert.FromHexString).ToArray();

    public static readonly byte[] ServerPublicKey = Convert.FromHexString("02596F0D0C061A8B30745988FD72C59E29EC367FB0F341F28E0F08D037BAFC69");
    public const int RegionTableLength = 12 * 40;

    public static bool IsRegionKeyTable(ReadOnlySpan<byte> data)
    {
        if (data.Length != RegionTableLength)
            return false;
        for (int i = 0; i < RegionPublicKeys.Length; ++i)
        {
            ReadOnlySpan<byte> row = data.Slice(i * 40, 40);
            if (System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(row) != i + 1 || row[36] != (i == 8 ? 0 : 1))
                return false;
            ReadOnlySpan<byte> key = row.Slice(4, 32);
            if (!key.SequenceEqual(RegionPublicKeys[i]) && !(i == 0 && key.SequenceEqual(ServerPublicKey)))
                return false;
        }
        return true;
    }

    public static void ValidateImage(byte[] image, (int Major, int Minor, int Revision, int Build) version, GameVersion branch)
    {
        if (branch != GameVersion.ClassicEra || version != (1, 15, 9, 70003))
            throw new InvalidOperationException("--experimental-70003 requires --version ClassicEra and client 1.15.9.70003.");

        if (Convert.ToHexString(SHA256.HashData(image)) != ImageSha256)
            throw new InvalidOperationException("Unverified 70003 executable: SHA-256 does not match the diagnostic profile. No client started.");
    }

    public static void ValidateMemory(byte[] data)
    {
        RequireUnique(data, Patterns.Common.Portal, "Login Portal");
        RequireUnique(data, Patterns.Windows.LauncherLogin, "Launcher Login Registry");
    }

    static void RequireUnique(byte[] data, short[] pattern, string name)
    {
        byte[] bytes = pattern.Select(value => checked((byte)value)).ToArray();
        int offset = data.AsSpan().IndexOf(bytes);
        if (offset < 0 || data.AsSpan(offset + 1).IndexOf(bytes) >= 0)
            throw new InvalidOperationException($"Expected exactly one {name} pattern. Diagnostic launch stopped before patching.");
    }
}

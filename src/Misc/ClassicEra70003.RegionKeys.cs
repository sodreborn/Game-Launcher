// Copyright (c) Burralis.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Diagnostics;
using System.Runtime.InteropServices;
using static Burralis.Game.Launcher.Misc.NativeWindows;

namespace Burralis.Game.Launcher.Misc;

static partial class ClassicEra70003
{
    [StructLayout(LayoutKind.Sequential)]
    struct RegionInfo
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern nuint VirtualQueryEx(nint process, nint address, out RegionInfo info, nuint length);

    static byte[] ReadTable(nint process, nint address)
    {
        byte[] data = new byte[RegionTableLength];
        return ReadProcessMemory(process, address, data, data.Length, out int read) && read == data.Length ? data : null;
    }

    static List<nint> FindRegionTables(nint process)
    {
        const int chunkSize = 4 * 1024 * 1024;
        byte[] buffer = new byte[chunkSize + RegionTableLength - 1];
        byte[][] firstKeys = [RegionPublicKeys[0], ServerPublicKey];
        var found = new HashSet<nint>();
        long address = 0;
        while (address < 0x00007FFFFFFFFFFF && VirtualQueryEx(process, (nint)address, out RegionInfo info, (nuint)Marshal.SizeOf<RegionInfo>()) != 0)
        {
            long start = (long)info.BaseAddress, end = checked(start + (long)info.RegionSize);
            if (info.State == 0x1000 && info.Type == 0x20000 && (info.Protect == 4 || info.Protect == 8))
            {
                for (long position = start; position < end; position += chunkSize)
                {
                    int length = (int)Math.Min(buffer.Length, end - position);
                    if (!ReadProcessMemory(process, (nint)position, buffer, length, out int read) || read < RegionTableLength)
                        continue;
                    // Use a vectorized key search, then validate all 12 records.
                    foreach (byte[] firstKey in firstKeys)
                    {
                        int offset = 0;
                        while (offset < read)
                        {
                            int relative = buffer.AsSpan(offset, read - offset).IndexOf(firstKey);
                            if (relative < 0)
                                break;
                            int rowStart = offset + relative - 4;
                            if (rowStart >= 0 && rowStart <= read - RegionTableLength && ((position + rowStart) & 7) == 0 && IsRegionKeyTable(buffer.AsSpan(rowStart, RegionTableLength)))
                                found.Add((nint)(position + rowStart));
                            offset += relative + 1;
                        }
                    }
                }
            }
            if (end <= address)
                break;
            address = end;
        }
        return found.ToList();
    }

    public static void MonitorRegionKey(nint processHandle, Process process)
    {
        Console.WriteLine("70003 region-key monitor active. The first realm attempt initializes its certificate store and may disconnect; retry after the key-ready message.");
        nint table = 0;
        bool announced = false;
        while (!process.HasExited)
        {
            byte[] before = table == 0 ? null : ReadTable(processHandle, table);
            if (before == null || !IsRegionKeyTable(before))
            {
                List<nint> candidates = FindRegionTables(processHandle);
                if (candidates.Count > 1)
                    throw new InvalidOperationException("Ambiguous 70003 certificate stores. No region key written.");
                table = candidates.Count == 1 ? candidates[0] : 0;
                before = table == 0 ? null : ReadTable(processHandle, table);
            }
            if (before != null && IsRegionKeyTable(before))
            {
                byte[] expected = (byte[])before.Clone();
                ServerPublicKey.CopyTo(expected, 4);
                if (!before.AsSpan(4, 32).SequenceEqual(ServerPublicKey))
                {
                    if (!WriteProcessMemory(processHandle, table + 4, ServerPublicKey, ServerPublicKey.Length, out int written) || written != ServerPublicKey.Length)
                        throw new InvalidOperationException("Unable to write the verified region-1 public key.");
                }
                if (ReadTable(processHandle, table)?.SequenceEqual(expected) != true)
                    throw new InvalidOperationException("Region-key readback failed. Local login remains unverified.");
                if (!announced)
                {
                    Console.WriteLine("Region-1 public key ready and verified. Signature checks remain enabled. Retry the realm if its first attempt disconnected.");
                    announced = true;
                }
            }
            Thread.Sleep(table == 0 ? 1000 : 100);
        }
    }
}

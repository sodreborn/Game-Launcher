// Copyright (c) Burralis.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Buffers.Binary;
using static Burralis.Game.Launcher.Misc.NativeWindows;

namespace Burralis.Game.Launcher.Misc;

static partial class ClassicEra70003
{
    static bool IsNativeTutorialStateInitialized(nint process, nint imageBase)
    {
        byte[] pointer = new byte[8];
        return ReadProcessMemory(process, imageBase + 0x4309270, pointer, pointer.Length, out int read)
            && read == pointer.Length && BinaryPrimitives.ReadUInt64LittleEndian(pointer) != 0;
    }

    // Explicit, exact-image diagnostic compatibility bridge, not a discovered
    // standard tutorial opcode. The unused 0x460268 decoder exposes its raw
    // packet bytes at +0x20, matching the verified native tutorial initializer.
    // Reuse that already registered native function; never write executable code
    // or fabricate the tutorial vectors. The server supplies exactly 32 bytes.
    static bool BindNativeTutorialInitializer(nint process, nint imageBase)
    {
        byte[] ReadPointer(nint address)
        {
            byte[] bytes = new byte[8];
            return ReadProcessMemory(process, address, bytes, bytes.Length, out int read) && read == bytes.Length ? bytes : null;
        }

        byte[] source = ReadPointer(imageBase + TutorialSourceSlotRva);
        byte[] before = ReadPointer(imageBase + TutorialBridgeSlotRva);
        if (source == null || before == null)
            return false;
        if (!ValidateTutorialBinding((ulong)imageBase, BinaryPrimitives.ReadUInt64LittleEndian(source), BinaryPrimitives.ReadUInt64LittleEndian(before)))
            return false;

        if (!source.SequenceEqual(before))
        {
            // Recheck registration immediately before touching the unused slot.
            if (ReadPointer(imageBase + TutorialSourceSlotRva)?.SequenceEqual(source) != true)
                return false;
            if (!WriteProcessMemory(process, imageBase + TutorialBridgeSlotRva, source, source.Length, out int written) || written != source.Length)
                throw new InvalidOperationException("Could not install the native tutorial bridge.");
        }
        if (ReadPointer(imageBase + TutorialBridgeSlotRva)?.SequenceEqual(source) != true)
            throw new InvalidOperationException("Tutorial bridge readback verification failed.");
        return true;
    }
}

# Burralis Game Launcher
A game launcher for World of Warcraft that allows you to connect to custom servers with a valid tls certificate attached.

## Local diagnostic mode for Classic Era 70003

`--experimental-70003 --version ClassicEra` enables a diagnostic profile for the locally inspected
1.15.9.70003 Windows executable with SHA-256
`d8ac9f021a099cde4f2f891b9fc82a3fd67c7c875e8e1a6c4cde1dfd3a6119be`.
The profile verifies unique portal and registry patterns before resuming the client. It then watches
the certificate-backed regional key table in writable private memory, validates all twelve IDs, public
keys and enabled flags, and replaces only region 1 with the local server public key. Every write is
read back; signature verification and trusted TLS remain enabled. The client executable and archives
stay unchanged. The former `.rdata` key pattern was two adjacent fragments from different keys and
is no longer written.

Keep the launcher running. The client initializes this table lazily: the first realm attempt may
disconnect before the monitor reports `Region-1 public key ready and verified`. Retry login and the
realm after that message. This initialization/retry requirement remains a diagnostic limitation.

The legacy RSA modulus pattern is absent in this executable. This mode leaves that patch unimplemented;
the matching 70003 server uses a modern endpoint-vector world redirect instead. Local testing has
reached realm selection, usable character creation/selection, instance authentication and world loading.
The additional Project SOD tutorial bootstrap bridge below has also been tested entering Northshire,
rotating the camera, and opening/dismissing a native tutorial, with normal world updates isolated.
Login remains intermittent (including WOW51900317 before the realm list), and normal gameplay is
unverified. This is an opt-in compatibility profile, not production-ready server support.
Other clients and launches without the flag retain the original patch requirements.

Example (use your own server host with a valid trusted TLS certificate):

```powershell
& '.\Burralis Game Launcher.exe' --experimental-70003 --version ClassicEra --path 'C:\Games\World of Warcraft\_classic_era_' -config ConfigSOD.wtf --portal your.realm.example
```

The replacement `ServerPublicKey` in `src/Misc/ClassicEra70003.cs` must match your server's
encryption-signing key. It is a public verification key; no server private key is included.
The tests use synthetic patterns and regional tables. To also check a local executable's exact hash:

```powershell
dotnet run --project tests/ClassicEra70003.Tests -c Release -- 'C:\Games\World of Warcraft\_classic_era_\WowClassic.exe'
```

### Optional Project SOD tutorial bootstrap bridge

`--experimental-70003-tutorial-bridge` additionally requires the verified 70003 profile and
`--portal projectsod.duckdns.org`. This is an explicit local protocol compatibility bridge,
not a confirmed standard mapping for the client's tutorial packet.

The server sends eight uint32 tutorial flags through the raw-buffer decoder at wire `0x460268`.
While that decoder's callback is empty, the launcher binds it to the client's already registered,
exact-version native tutorial initializer. The source pointer, destination state and write readback
are checked. Once the native tutorial array exists, bridge monitoring ends: world initialization
registers a different native callback in that slot, which must remain untouched. No executable code
bytes or tutorial-array contents are written by the bridge.

The matching server must send this message only during initial account setup, before character
selection, and omit the duplicate after the player exists. Keep the client at character selection
until the launcher reports `Native tutorial array initialized`. Use a fresh client for each
diagnostic run. The earlier Lua tutorial-guard addon is insufficient for native camera input and
should remain disabled. Other gameplay opcodes and update messages are still under validation.

```powershell
& '.\Burralis Game Launcher.exe' --experimental-70003 --experimental-70003-tutorial-bridge --version ClassicEra --path 'C:\Games\World of Warcraft\_classic_era_' -config ConfigSOD.wtf --portal projectsod.duckdns.org
```

### License & Contributions

Licensed under the MIT license. See the [LICENSE](LICENSE) file for full license information.

## Important Scope & Limitations

This launcher has a very strict and intentionally limited feature set.

### What is not included
- No developer mode
- No mod loading
- No general compatibility guarantees for unverified experimental or legacy client builds
- No support for deprecated client branches

### What This Launcher Can Do

- Launch modern World of Warcraft clients
- Connect to custom servers with a valid TLS certificate attached
- Allow custom client version & cdn urls  
  Useful for launching older clients or serving data from your own CDN

## Supported Clients
| Client Branch | Min Supported Version | Max Supported Version |
|-------------------------|------------|------------|
| Mainline                | 10.1.5     | **12.0.5** |
| Classic Era             | 1.14.4     | **\*** |
| Classic                 | 3.4.2      | **5.5.3** |
| Classic Anniversary     | 2.5.5      | **\*** |
| Classic Titan           | 3.80.0     | **\*** |

**\* = all future versions in that branch (unless otherwise stated)**

### NOTE FOR SERVER CONNECTIONS
* A valid certificate matching your authentication/bnet server host name.
  That certificate needs to be loaded by the authentication/bnet server too

## Building

### Build Prerequisites
* [.NET Core SDK 10.0.0 or later](https://dotnet.microsoft.com/download/dotnet/10.0)
* Optional for native builds: C++ workload through Visual Studio 2026 or latest C++ build tools

### Build Instructions Windows (native)
* Execute `dotnet publish -r win-x64 -c Configuration -p:platform="x64" -p:PublishAot=true`
* Native output is placed in the `build` folder.

## Usage

### Windows Usage
1. Copy `Burralis Game Launcher.exe` to your World of Warcraft folder.
2. Edit the `WTF/Config.wtf` to set your portal or use a different config file with the `-config Config2.wtf` launch arg.
3. Run the `Burralis Game Launcher.exe`

### Launch Parameters
Use `--help`

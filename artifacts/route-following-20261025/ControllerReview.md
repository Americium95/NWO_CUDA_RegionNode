# Controller route-following harness review

Date: 2026-10-25

The standalone harness now stores V2 routes through `AutopilotController.StoreValidatedRoute`, toggles them through `AutopilotController.Toggle`, and advances the real `LockstepMovement.AdvanceTick` implementation. Its fixtures use a `DotNetty EmbeddedChannel` pipeline context with a capture handler and a fake authoritative navmesh provider returning fixture centroids. No production C# files were edited.

Run: `dotnet run --project tests/RouteFollowingHarness/RouteFollowingHarness.csproj -c Release`

Superseded heading observation: the earlier 12/13 result and byte-192 claim were erroneous. The exhaustive wire-heading check now passes for every tenth of a degree from 0 through 3600 tenths, comparing against all 256 encoded byte headings. The production convention is `HeadingDegrees(byte) = byte * 1.4` degrees; east resolves to byte 193 (270.2 decoded degrees, circular error 0.2 degrees from the target angle of 270 degrees), and north resolves to byte 0. The wire circle is 358.4 degrees with a circular seam; replacing it with 360/256 is not the tested production convention and no production edit is justified by the prior claim.

The real-tick tiny-leg/sharp-corner scenario passed with observed active cross-track 0.000 units (limit sqrt(2) for integer-coordinate quantization), then recorded final forward carry along the final tangent. Terrain barrier rollback, translated floating origin, off/on route state, external-position-change fail-closed behavior, manual lockstep movement, and unwrapped dateline fixture passed. JSON tick traces include actual positions, headings, route active state, and terrain callback argument pairs.

Terrain callback coordinates are intentionally reported as observed callback arguments only. `Terrain.terrainCollision` subtracts fixed offsets before selecting an external bitmap under a machine-specific path, so this harness does not establish a verified mapping from callback coordinates to world-origin coordinates.

Final bounded harness verification (2026-10-25): 3,642/3,642 checks pass; see `FinalController.md`, `HeadingAndMovement.results.json`, and `HeadingAndMovement.ticktrace.json` for the complete result and per-tick trace.

Build emitted the expected NETSDK1138 notice that net6.0 is out of support; compilation succeeded. Artifacts: `ControllerReview.ticktrace.json` and `ControllerReview.results.json`.

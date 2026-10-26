# Server route following: implementation evidence

Date: 2026-10-25  
Workspace: `C:\GitHub\NWO_CUDA_RegionNode`  
Task: `task_a9962bd9062e` / Dispatch `ctx_f88c7a3c9e61`

## Actual cause and scoped fixes

The movement path had two independent issues. `AutopilotController` encoded target degrees using 1.406 degrees per byte, terrain probing used the same factor, while `LockstepMovement.ApplyMovementStep` integrated at 1.4 degrees per byte. The client encodes control bytes using 1.4/1.405 and decodes authoritative movement rotation at 1.4, so the server now uses one 1.4-degree scale for target encoding, terrain probing, and movement integration. Cardinal heading and 0/255 wrap checks exercise the mapping.

V2 route progress previously accepted any waypoint within 160 world units, irrespective of whether the vessel passed it. A production-formula simulation of a 500-by-500 L route reproduced an orbit that failed to advance after 1,836 ticks, with maximum distance to the route of 792.18 units. V2 now advances only after passing the current segment endpoint within 40 units, rejects lateral misses beyond that tolerance, and reduces its target speed to 10 while approaching turns sharper than 30 degrees. The reduction uses the existing 5-unit/tick deceleration and 0.4 world-units-per-speed-unit displacement model; cruise speed remains 100 on straight legs. V1 retains its 160-unit behavior. Completion still resumes forward sailing at current speed and heading.

## Coordinate-frame finding

No blind global-origin rebasing was made. Client `0x04/0` sends object-relative X/Z and no global nav origin; V2 waypoints are global navmesh coordinates. The map UI publishes ship position from a separate MiniMap-calibrated coordinate source and rejects routes whose start is more than 8 nav pixels from the current position. Server `StoreValidatedRoute` therefore anchors waypoint zero to the authoritative server position at packet receipt, before the client requests the navigation toggle. This is the established floating-origin bridge in the current protocol. The precise receipt-time client/server offset cannot be reconstructed from the existing wire fields. A future exact correction requires a protocol field pairing the submitted route start with a ship-position sample in a documented shared frame, plus client compatibility work.

## Runnable regression evidence

Run from the repository root:

```powershell
dotnet run --project tests/RouteFollowingHarness/RouteFollowingHarness.csproj --configuration Release --property:DefaultItemExcludesInProjectFolder=obj/**%3Bbin/** --no-restore
```

Result: all 17 harness checks passed. They cover four heading directions, canonical heading scale, byte-seam turning, segment projection/endpoint clamping, tiny-segment start and pass, sharp-corner lateral rejection, corner braking, translated floating-origin route arithmetic, accepted unwrapped dateline waypoint continuity, L-route tracing, and forward-sailing continuation.

Measured L-route using the production heading, turn, braking, waypoint-pass, and position-step formulas: **91 ticks**, **47.00 units maximum cross-track distance to the route polyline**, **11.40 units endpoint error**. The trace is deterministic simulation evidence, not a live server/client observation. Initial no-braking trace failed to progress (1,836 ticks, 792.18 units max cross-track, 36 units endpoint error); it led to the V2-only corner-speed correction.

The build emitted existing net6 end-of-support, nullable, and Windows platform analyzer warnings; it built and ran successfully. Build outputs were produced under project `bin/obj` paths. Those paths already contained extensive dirty artifacts before this task; their prior contents were not snapshotted, so no attribution or restoration is claimed. No live app/server was started or restarted.

## Files changed by this task

- `NWO_RegionNode/AutopilotController.cs`
- `NWO_RegionNode/LockstepMovement.cs`
- `tests/RouteFollowingHarness/Program.cs`
- `tests/RouteFollowingHarness/RouteFollowingHarness.csproj`
- `artifacts/route-following-20261025/CodexImplementation.md`

No client files, mesh assets, pin/config files, or unrelated source files were edited.

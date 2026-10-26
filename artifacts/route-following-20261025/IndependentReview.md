# Independent route-following review

Date: 2026-10-25
Workspace: `C:\GitHub\NWO_CUDA_RegionNode`; client reference `C:\GitHub\NWO`.
Scope: read-only review of server route intake, waypoint conversion, steering, lockstep movement, and available client coordinate/movement source. No production files changed; no build, runtime, or live-server actions performed.

## Evidence and provenance

SHA256 (current workspace contents):

- `NWO_RegionNode/AutopilotController.cs` `449C85BFEDED8255118A19D8787A45C35F31E345447EB8D6EA8E4FFB2F934445`
- `NWO_RegionNode/LockstepMovement.cs` `EF924998B6033A4ACC1B85C1C0BF0096A48BC9FC4B16068D899EC9BC8061CF2C`
- `NWO_RegionNode/PathPolygonDecoder.cs` `73CFD0E943BF63113029F9956FE8CE8EB572854DF095B7AABB64EAA3B4DC365E`
- `NWO_RegionNode/NavigationFrameHandler.cs` `4FCE2E1FBF087C92066D6CE81975F6D8C46E048EC4762A3D52F5C09054A2CB19`
- `C:\GitHub\NWO\Assets\ship\ShipAI\GeoNavCoordinateSystem.cs` `06ED06372B3AFAEE923DFA35656929A2792A01B7414334F8E6344982659AC458`
- `C:\GitHub\NWO\Assets\ship\ShipMove.cs` `1505BDF3C0FAE18C1EFFCFE1CFBA77C11DAEAA36F38E594CB47DFF4F818E8D53`

## Findings

### 1. V2 route anchoring loses the ship's planned start offset (strong defect, subject to protocol intent)

`AutopilotController.StoreValidatedRoute(PathPolygonIndexRequest, ...)` stores `StartNavX/Y = points[0]` and `StartWorldX/Z = ship.globalPosition` (lines 114–118). Every subsequent target is computed as store-time world position plus the delta from waypoint zero (lines 182–183); waypoint zero itself is skipped (`Next = 1`, line 114). This means the planned first nav waypoint is forcibly made identical to the ship position at storage, regardless of the actual difference between the submitted waypoint and ship position. If the packet's nav coordinates are global map coordinates—as the client constructs `navRoute` from selected path points and server validates them against the global navmesh—the correct mapping must use the shared nav-to-world origin/transform, not rebase waypoint zero on the ship. The current code therefore erases the initial nav offset and translates every later waypoint by `shipWorld - world(firstWaypoint)`.

Concrete equation: with nav-to-world X scale 1280, actual first point world X=1000, ship-at-store X=1100, second point X=1001, current target X=1100 + (1001-1000)*1280 = 2380; the global-frame target would be 1000 + 1280 = 2280. Error is +100 world units, equal to initial ship-to-route offset. (A one-unit nav displacement was chosen only to expose the translation; no client runtime was available.) Confirmed intake order: `NavigationFrameHandler` passes the decoded request to `StoreValidatedRoute` (lines 35–38); route is therefore anchored at receipt/storage time, before the client's subsequent toggle request. Exact shift at a real route depends on ship position and first waypoint at storage.

Minimal correction: retain the shared nav-to-world transform/origin or send an explicit validated ship-start correspondence; do not infer map origin from ship position at request receipt. If client/server intentionally define waypoint zero as a local origin, document that wire contract and validate the intended ship-start relationship before rebasing. Do not change legacy V1 polygon-centroid behavior without separate evidence.

### 2. Heading scale mismatch creates systematic directional error (proven code mismatch; magnitude bounded)

Steering maps the direction to a byte angle by dividing degrees by 1.406 (`AutopilotController.cs:195`). Lockstep turning and terrain probing interpret a byte using 1.406 degrees (`LockstepMovement.cs:16`), but actual position integration uses 1.4 degrees (`LockstepMovement.cs:41–43`). Thus a stable byte target reached by `MoveTowardsAngle` has movement bearing 1.4/1.406 of the intended bearing. At byte 64, intended 89.984° versus integrated 89.6° (difference 0.384°); at byte 128 the difference is 0.768°; at 255, 1.53°. Over a 10,000-unit straight leg, a constant 0.768° gives about 134 units cross-track (before integer truncation or steering updates). At turns, target selection and movement both use the same 1.406 quantization except for movement integration, so the error varies with heading and can accumulate.

Minimal correction: use one byte-to-degree constant across steering, terrain probing, movement integration, and packet conversion, after confirming the established wire/client convention. The client `ShipMove.cs:176` divides visual `rot` by 1.405f, another slight mismatch; it is not evidence that 1.4 is the intended server value. This is a deterministic source mismatch, but perceived symptom severity needs route/runtime evidence.

### 3. Waypoint radius and 400 ms tick

The server considers a target reached inside squared distance 25,600 (radius 160 world units) in `AutopilotController.cs:186`. `Program` invokes `LockstepMovement.AdvanceTick` on the movement timer; in the currently visible `Program.cs`, the route advances at the shared lockstep cadence (existing report records 400 ms, but this review did not independently verify the timer declaration). At speed 100, the position integrator's nominal per-tick displacement is 40 units before integer truncation (`speed*400/1000`), so the radius spans roughly four nominal steps. A long interval alone does not prove waypoint skipping. Radius acceptance can cut corners by up to 160 units, and the 4-byte-angle-unit turn cap limits heading change to about 5.624° per tick under the 1.406 convention. For a short sharp turn, this can pass the waypoint neighborhood with a large cross-track miss; no isolated executable simulation was run, so classify the actual miss rate as unverified. Minimal recommendation: log closest approach/next-waypoint distances on representative routes, then tune arrival/lookahead and slow before corners based on those measurements.

### 4. Dateline unwrap

V2 decoder checks adjacent X deltas against half the map width and Y against map size (`PathPolygonDecoder.cs`, waypoint validation loop). This only rejects oversized jumps; it does not itself unwrap. Client coordinate class documents periodic wrapped-X distances, and current `ShipMove.cs` references `WorldXZToNav` and wrapped route math. Server V2 controller then directly subtracts `points[0].X` from target waypoint X, with no periodic shortest-copy adjustment. If accepted packet points straddle a seam without being lifted to a consistent periodic copy, a short seam crossing can be interpreted in the wrong direction/long way; if client `PathPlanRouteV2` has already unwrapped them, no defect follows. The required client route builder source was not inspected here, so this remains a protocol-dependent hypothesis, not a proven failure.

### 5. Post-arrival forward movement is explicit behavior

Final waypoint acceptance increments `Next`, detects end, and calls `Cancel`; `Cancel` calls `ResumeForwardSailing`, which sets `targetAngle=rot` and `targetspeed=max(0,speed)` (`AutopilotController.cs:126–142, 188–190). It therefore keeps the vessel moving forward at its current speed/heading after route completion. This is proven behavior, not a defect absent evidence that the intended completion mode is stop. Preserve it if the user expects forward sailing; a stop-on-arrival change would be a product behavior decision, not a correctness fix.

## Checks performed and limits

Read the named server source files, `NavigationFrameHandler.cs`, relevant `Program.cs` call site, and client `GeoNavCoordinateSystem.cs` / `ShipMove.cs`; traced intake into route storage and storage into lockstep target/movement equations; computed hashes above. No tests, builds, server restarts, external sends, or edits outside this report were made. The earlier `REPORT.md` contains control-character/escape-corrupted rendering and simulations that this review did not rerun; its reported simulation outcomes are not relied upon here. Client source is a separate checkout, and this review did not inspect all route generation code or run an end-to-end route, so dateline behavior and real-world route deltas remain to be confirmed against an actual submitted packet and store-time ship snapshot.

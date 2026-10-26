# V2 cursor recovery (2026-10-25)

## Changes

- V2 advances its server position directly and now writes the byte heading to both `rot` and `targetAngle`, preserving the final route tangent for ordinary forward sailing.
- Remaining tick budget after the final waypoint is integrated once along that final tangent. The carry path is terrain sampled at intervals no larger than 5 world units.
- V2 movement is planned in local cursor/waypoint state. The complete plan is terrain sampled at intervals no larger than 5 units, including endpoints, before route cursor or position is committed. A blocked plan leaves cursor, position, and progress unchanged and applies the existing stop behavior.
- Reconnect checks only the current segment (2 squared world units tolerance for integer rounding), never searches future segments, and fails closed when the ship is elsewhere. Toggle ON rejects an off-segment ship without teleporting it.
- Zero length legs advance through the finite waypoint array; active V2 ticks keep target speed at 100.
- Receipt-time bridge and protocol were left unchanged; legacy V1 and manual lockstep paths remain intact.

## Terrain frame limitation

The terrain callback accepts integer world X and negated world Z coordinates. Route waypoint Y is transformed to world Z using the existing V2 scale and start anchor, while terrain height's legacy frame/origin semantics are not explicit in this callback. This change samples using the established callback convention; it does not blindly rebase terrain coordinates. Confirm the terrain frame against production data in the separate controller-harness task.

## Verification

Command: `dotnet build NWO_RegionNode/NWO_RegionNode.csproj -c Release`

Result: succeeded, 0 errors and 58 warnings (including existing nullable, platform, and net6 support warnings). No controller harness was added or run; that is assigned to the next independent task.

## Remaining risk

The final-tangent excess budget is carried as ordinary forward travel within this tick, with terrain sampling, then later ticks use legacy movement. Heading byte quantization and the terrain frame convention remain dependent on existing production behavior and need dedicated harness/runtime validation.

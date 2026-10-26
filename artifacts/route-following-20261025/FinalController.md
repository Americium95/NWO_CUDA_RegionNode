# Final route-following controller harness

Date: 2026-10-25

Run: `dotnet run --project tests/RouteFollowingHarness/RouteFollowingHarness.csproj -c Release`

Result: **3,642/3,642 checks passed**, zero failures. The 500-unit V2 elbow was checked after every active tick from 1 through 25; maximum distance to either fixture segment was **0.000 world units** (limit `sqrt(2)`, approximately 1.414). Tick 25 ended exactly at `(500,-500)` with the route inactive; tick 26 advanced normally to `(500,-540)`. The trace records each tick's position, byte heading, active flag, and cross-track distance.

The harness also exercises the actual V1 `StoreValidatedRoute(uid, oid, new[]{1,2}, ctx)` and `Toggle(..., true, ...)` path using fake centroids `(0,0)` and `(0,1)`. The first tick is `(0,-40)` with the route stored; OFF pauses route tracking while normal forward movement continues; cancel removes the route and the following ordinary tick advances forward. The legacy 160-unit arrival smoke stores at origin, moves the ship to `(0,-1200)`, and toggles toward the anchored `(0,-1280)` target; the route completes inside the legacy radius. Existing tiny-route and terrain-barrier checks remain in the suite.

The exhaustive heading check passes across all 3,601 tenth-degree inputs. East target resolves to byte **193**, decoded as 270.2 degrees under the production `byte * 1.4` convention (0.2-degree circular error); north resolves to byte 0. This corrects the prior erroneous byte-192 / 90-degree / 360-over-256 claim in `ControllerReview.md`. No production source files were edited.

The Release run emitted the existing NETSDK1138 notice that the projects target unsupported net6.0; the build and harness completed successfully.

SHA-256:

- `tests/RouteFollowingHarness/Program.cs`: `897E83750A26ABF2813E3BDA28669D751235E0C542846A7AFDA9D1D3FB70AC8D`
- `artifacts/route-following-20261025/ControllerReview.md`: `81C7E62EC9CB817631108F5331477DF940FC8389EC1C0F258932C6196B2E7B23`
- `artifacts/route-following-20261025/HeadingAndMovement.results.json`: `F72C95DABE2A025C15F1A9325C77862D494AD9EE97688C91796B293EEA0CADA5`
- `artifacts/route-following-20261025/HeadingAndMovement.ticktrace.json`: `E15EEDE27F499BA0C73E90D3B835973C2F9F61844DA21B10B69B686D65493658`

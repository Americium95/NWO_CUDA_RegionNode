using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DotNetty.Transport.Channels;
using NWO_RegionNode.PathValidation;

namespace NWO_RegionNode
{
    /// <summary>Server-side route state in authoritative MoveMent globalPosition coordinates.</summary>
    public sealed class AutopilotController
    {
        private const int AutopilotTargetSpeed = 100;
        private readonly PathValidator _validator;
        private readonly Dictionary<uint, Route> _routes = new();
        private sealed class Route
        {
            public uint UserId;
            public int[] Polygons = Array.Empty<int>();
            public Vector2[] Waypoints = Array.Empty<Vector2>();
            public bool UsesV2Waypoints;
            public int Next;
            public bool Active;
            public IChannelHandlerContext Owner;
            public double StartNavX, StartNavY;
            public int StartWorldX, StartWorldZ;
            public double CursorX, CursorZ;
            public bool CursorInitialized;
        }

        public AutopilotController(IAuthoritativeNavMeshProvider provider) { _validator = new PathValidator(provider); }
        public bool TryGetShip(uint objectId, out MoveMent ship)
        {
            ship = null;
            return Program.moveMentTable.TryGetValue(objectId, out ship);
        }

        public bool Toggle(uint userId, uint objectId, uint requestId, bool enabled, IChannelHandlerContext context)
        {
            if (!Program.moveMentTable.TryGetValue(objectId, out MoveMent ship))
                return false;
            if (enabled)
            {
                if (!_routes.TryGetValue(objectId, out Route route)
                    || (route.UsesV2Waypoints ? route.Waypoints.Length < 2 : route.Polygons.Length < 2)) return false;
                if (route.Next == 0) route.Next = 1;
                if (route.Next >= (route.UsesV2Waypoints ? route.Waypoints.Length : route.Polygons.Length)) return false;
                route.Active = true;
                route.UserId = userId;
                if (route.UsesV2Waypoints && !ReconnectV2(route, ship)) { route.Active = false; return false; }
                else AdvanceRoute(objectId, ship, route);
            }
            // OFF pauses the stored server-validated route so a later ON can
            // steer again from the ship's current authoritative position.
            else Pause(objectId);
            return true;
        }

        public bool StoreValidatedRoute(uint userId, uint objectId, int[] polygons, IChannelHandlerContext context)
        {
            if (polygons == null || polygons.Length < 2)
            {
                Console.WriteLine($"[navigation] route storage rejected: invalid polygon list userId={userId}");
                return false;
            }
            if (!Program.moveMentTable.TryGetValue(objectId, out MoveMent ship))
            {
                Console.WriteLine($"[navigation] route storage rejected: object not registered userId={userId} objectId={objectId}");
                return false;
            }
            if (!_validator.TryGetCentroid(polygons[0], out double startNavX, out double startNavY))
            {
                Console.WriteLine($"[navigation] route storage rejected: start polygon has no centroid userId={userId} shipId={ship.id} startPolygon={polygons[0]}");
                return false;
            }
            for (int i = 1; i < polygons.Length; i++)
                if (!_validator.TryGetCentroid(polygons[i], out _, out _))
                {
                    Console.WriteLine($"[navigation] route storage rejected: polygon has no centroid userId={userId} shipId={ship.id} polygon={polygons[i]}");
                    return false;
                }

            // The legacy 0x04/0 position packet is relative to the client's floating
            // origin, while polygon coordinates are in the global navmesh frame.
            // Anchor this validated route's navmesh origin to the ship's authoritative
            // current server position, then apply navmesh deltas while steering.
            // The route request carries polygon IDs only, so server-side containment
            // of the start polygon cannot be checked in the client's navmesh frame.
            _routes[objectId] = new Route
            {
                UserId = userId,
                Polygons = (int[])polygons.Clone(),
                Owner = context,
                StartNavX = startNavX,
                StartNavY = startNavY,
                StartWorldX = ship.globalPosition.X,
                StartWorldZ = ship.globalPosition.Z
            };
            return true;
        }

        public bool StoreValidatedRoute(PathPolygonIndexRequest request, IChannelHandlerContext context)
        {
            if (request == null || request.ProtocolVersion != 2 || request.PathPolygonIndices == null
                || request.PathPolygonIndices.Length < 2 || request.Waypoints == null || request.Waypoints.Length < 2
                || !Program.moveMentTable.TryGetValue(request.ObjectId, out MoveMent ship)) return false;
            for (int i = 0; i < request.Waypoints.Length; i++)
                if (!float.IsFinite(request.Waypoints[i].X) || !float.IsFinite(request.Waypoints[i].Y)) return false;

            Vector2[] points = (Vector2[])request.Waypoints.Clone();
            _routes[request.ObjectId] = new Route
            {
                UserId = request.UserId,
                Polygons = (int[])request.PathPolygonIndices.Clone(),
                Waypoints = points,
                UsesV2Waypoints = true,
                Next = 1,
                Owner = context,
                StartNavX = points[0].X,
                StartNavY = points[0].Y,
                StartWorldX = ship.globalPosition.X,
                StartWorldZ = ship.globalPosition.Z
            };
            return true;
        }

        public bool HasRoute(uint objectId) => _routes.ContainsKey(objectId);

        public void Cancel(uint objectId)
        {
            _routes.Remove(objectId);
            ResumeForwardSailing(objectId);
        }

        public void Pause(uint objectId)
        {
            if (_routes.TryGetValue(objectId, out Route route)) route.Active = false;
            ResumeForwardSailing(objectId);
        }

        private static void ResumeForwardSailing(uint objectId)
        {
            if (!Program.moveMentTable.TryGetValue(objectId, out MoveMent ship)) return;
            // Autopilot changes the steering target, but ordinary sailing has no
            // separately stored heading: it proceeds forward at the current speed.
            // Restore that movement mode without changing position or speed.
            ship.targetAngle = ship.rot;
            ship.targetspeed = (int)MathF.Max(0, ship.speed);
        }

        public void CancelByShip(uint objectId) => Cancel(objectId);

        public void CancelChannel(IChannelHandlerContext context)
        {
            if (context?.Channel == null) return;
            foreach (var pair in _routes.ToArray())
                if (pair.Value.Owner?.Channel != null && pair.Value.Owner.Channel.Id == context.Channel.Id)
                    Cancel(pair.Key);
        }

        public void Advance()
        {
            foreach (var pair in _routes.ToArray())
            {
                if (!pair.Value.Active) continue;
                if (pair.Value.Owner?.Channel == null || !Program.moveMentTable.TryGetValue(pair.Key, out MoveMent ship)) { Cancel(pair.Key); continue; }
                AdvanceRoute(pair.Key, ship, pair.Value);
            }
        }

        internal bool AdvanceV2Tick(uint objectId, MoveMent ship, Func<int, int, float> terrainHeight)
        {
            if (!_routes.TryGetValue(objectId, out Route route) || !route.Active || !route.UsesV2Waypoints) return false;
            return AdvanceV2(route, ship, terrainHeight);
        }

        private void AdvanceRoute(uint objectId, MoveMent ship, Route route)
        {
            double nx, ny;
            if (route.UsesV2Waypoints)
            {
                return;
            }
            else
            {
                if (route.Next >= route.Polygons.Length) { Cancel(objectId); return; }
                if (!_validator.TryGetCentroid(route.Polygons[route.Next], out nx, out ny)) { Cancel(objectId); return; }
            }
            double targetWorldX = route.StartWorldX + (nx - route.StartNavX) * 1280.0;
            double targetWorldZ = route.StartWorldZ - (ny - route.StartNavY) * 1280.0;
            double dx = targetWorldX - ship.globalPosition.X;
            double dz = targetWorldZ - ship.globalPosition.Z;
            if (dx * dx + dz * dz < LockstepMovement.LegacyWaypointArrivalRadius * LockstepMovement.LegacyWaypointArrivalRadius)
            {
                route.Next++;
                if (route.Next >= route.Polygons.Length) { Cancel(objectId); return; }
                AdvanceRoute(objectId, ship, route);
                return;
            }
            // Lockstep integration travels along (-sin(angle), -cos(angle)).
            ship.targetAngle = LockstepMovement.HeadingToTarget(dx, dz);
            ship.targetspeed = AutopilotTargetSpeed;
        }

        private static (double X, double Z) WorldPoint(Route route, int index) =>
            (route.StartWorldX + (route.Waypoints[index].X - route.StartNavX) * PathPolygonDecoder.V2_NAV_SCALE,
             route.StartWorldZ - (route.Waypoints[index].Y - route.StartNavY) * PathPolygonDecoder.V2_NAV_SCALE);

        private static bool ReconnectV2(Route route, MoveMent ship)
        {
            double x = ship.globalPosition.X, z = ship.globalPosition.Z;
            int next = Math.Max(1, route.Next);
            var a = WorldPoint(route, next - 1); var b = WorldPoint(route, next);
            double distanceSquared = LockstepMovement.DistanceSquaredToSegment(x, z, a.X, a.Z, b.X, b.Z);
            if (distanceSquared > 2d) return false;
            route.CursorX = x; route.CursorZ = z; route.CursorInitialized = true;
            route.Next = next;
            return true;
        }

        private static bool AdvanceV2(Route route, MoveMent ship, Func<int, int, float> terrainHeight)
        {
            if (!route.CursorInitialized && !ReconnectV2(route, ship)) { route.Active = false; ship.targetspeed = 0; return true; }
            if (route.CursorInitialized && (Math.Abs(ship.globalPosition.X - Math.Round(route.CursorX)) > 1 || Math.Abs(ship.globalPosition.Z - Math.Round(route.CursorZ)) > 1))
            {
                if (!ReconnectV2(route, ship)) { route.Active = false; ship.targetspeed = 0; return true; }
            }
            double cursorX = route.CursorX, cursorZ = route.CursorZ;
            float acceleration = ship.speed <= AutopilotTargetSpeed ? LockstepMovement.AccelerationPerTick : LockstepMovement.DecelerationPerTick;
            ship.speed = (float)MathR.MoveTowards(ship.speed, AutopilotTargetSpeed, acceleration);
            double budget = Math.Max(0d, ship.speed * LockstepMovement.TickSeconds);
            var samples = new List<(double X, double Z)> { (cursorX, cursorZ) };
            byte finalHeading = ship.rot;
            int zeroLengthLegs = 0;
            while (route.Next < route.Waypoints.Length && budget > 0)
            {
                var target = WorldPoint(route, route.Next);
                double dx = target.X - cursorX, dz = target.Z - cursorZ;
                double len = Math.Sqrt(dx * dx + dz * dz);
                if (len <= 1e-9) { route.Next++; if (++zeroLengthLegs >= route.Waypoints.Length) break; continue; }
                double step = Math.Min(len, budget);
                int count = Math.Max(1, (int)Math.Ceiling(step / 5d));
                for (int j = 1; j <= count; j++) samples.Add((cursorX + dx / len * step * j / count, cursorZ + dz / len * step * j / count));
                cursorX += dx / len * step; cursorZ += dz / len * step; budget -= step;
                finalHeading = LockstepMovement.HeadingToTarget(dx, dz);
                if (step >= len - 1e-9) route.Next++;
                else break;
            }
            foreach (var sample in samples)
                if (terrainHeight((int)Math.Truncate(sample.X), -(int)Math.Truncate(sample.Z)) >= 0.5f)
                { ship.targetspeed = 0; ship.speed = -1; return true; }
            route.CursorX = cursorX; route.CursorZ = cursorZ;
            ship.globalPosition.X = (int)Math.Round(cursorX, MidpointRounding.ToEven);
            ship.globalPosition.Z = (int)Math.Round(cursorZ, MidpointRounding.ToEven);
            ship.rot = finalHeading;
            ship.targetAngle = finalHeading;
            if (route.Next >= route.Waypoints.Length)
            {
                route.Active = false;
                // Apply any budget beyond the final waypoint once, using the last
                // route tangent. This tick bypasses ordinary lockstep integration.
                double remaining = budget;
                if (remaining > 0)
                {
                    double rad = LockstepMovement.HeadingDegrees(finalHeading) * Math.PI / 180d;
                    double endX = cursorX - Math.Sin(rad) * remaining, endZ = cursorZ - Math.Cos(rad) * remaining;
                    int count = Math.Max(1, (int)Math.Ceiling(remaining / 5d));
                    for (int i = 1; i <= count; i++)
                        if (terrainHeight((int)Math.Truncate(cursorX + (endX - cursorX) * i / count), -(int)Math.Truncate(cursorZ + (endZ - cursorZ) * i / count)) >= 0.5f)
                        { route.Active = true; ship.targetspeed = 0; ship.speed = -1; return true; }
                    cursorX = endX; cursorZ = endZ;
                    route.CursorX = cursorX; route.CursorZ = cursorZ;
                    ship.globalPosition.X = (int)Math.Round(cursorX, MidpointRounding.ToEven);
                    ship.globalPosition.Z = (int)Math.Round(cursorZ, MidpointRounding.ToEven);
                }
                ship.targetspeed = (int)MathF.Max(0, ship.speed);
                return true;
            }
            return true;
        }
    }
}

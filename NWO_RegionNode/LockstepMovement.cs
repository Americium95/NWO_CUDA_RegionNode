using System;
using System.Collections.Generic;

namespace NWO_RegionNode
{
    /// <summary>The shared production lockstep tick used by Program and the deterministic harness.</summary>
    public static class LockstepMovement
    {
        // The legacy client encodes heading as rot / 1.4 and movement snapshots
        // decode it as byte * 1.4 degrees. Keep server steering and integration
        // on that same wire convention.
        public const float DegreesPerHeadingUnit = 1.4f;
        public const float LegacyWaypointArrivalRadius = 160f;
        public const int TickMilliseconds = 200;
        public const double TickSeconds = TickMilliseconds / 1000d;
        public static readonly float AccelerationPerTick = 1.25f * (float)TickSeconds;
        public static readonly float DecelerationPerTick = 12.5f * (float)TickSeconds;

        public static float HeadingDegrees(byte heading) => heading * DegreesPerHeadingUnit;

        public static byte HeadingToTarget(double dx, double dz)
        {
            double degrees = Math.Atan2(-dx, -dz) * 180.0 / Math.PI;
            degrees = (degrees % 360.0 + 360.0) % 360.0;

            // The wire convention spans 256 * 1.4 = 358.4 degrees. Compare
            // actual decoded headings circularly, including the 357..360 seam.
            int best = 0;
            double bestError = double.PositiveInfinity;
            for (int candidate = 0; candidate <= 255; candidate++)
            {
                double actual = HeadingDegrees((byte)candidate);
                double delta = Math.Abs(actual - degrees);
                double error = Math.Min(delta, 360.0 - delta);
                if (error < bestError) { bestError = error; best = candidate; }
            }
            return (byte)best;
        }

        public static double DistanceSquaredToSegment(double px, double pz, double ax, double az, double bx, double bz)
        {
            double vx = bx - ax, vz = bz - az;
            double lengthSquared = vx * vx + vz * vz;
            double t = lengthSquared <= 0d ? 0d : ((px - ax) * vx + (pz - az) * vz) / lengthSquared;
            t = Math.Max(0d, Math.Min(1d, t));
            double dx = px - (ax + t * vx), dz = pz - (az + t * vz);
            return dx * dx + dz * dz;
        }

        public static void AdvanceTick(AutopilotController? autopilot, IEnumerable<MoveMent> movements, Func<int, int, float> terrainHeight)
        {
            autopilot?.Advance();
            foreach (MoveMent movement in movements)
            {
                if (autopilot != null && autopilot.AdvanceV2Tick(movement.id, movement, terrainHeight)) continue;
                movement.rot = (byte)((MathR.MoveTowardsAngle(movement.rot, movement.targetAngle, 2) + 256) % 256);
                float maxH = float.MinValue;
                float rad = HeadingDegrees(movement.rot) * MathF.PI / 180f;
                float dirX = MathF.Sin(rad), dirZ = MathF.Cos(rad);
                for (int d = 5; d <= 100; d += 10)
                {
                    MoveMent.nwo_Vector3 pos = movement.globalPosition + new MoveMent.nwo_Vector3((int)(dirX * -d), 0, (int)(dirZ * -d));
                    float h = terrainHeight(pos.X, -pos.Z);
                    if (h > maxH) maxH = h;
                }
                if (maxH < 0.5f)
                {
                    float acceleration = movement.speed <= movement.targetspeed ? AccelerationPerTick : DecelerationPerTick;
                    movement.speed = (float)MathR.MoveTowards(movement.speed, movement.targetspeed, acceleration);
                    ApplyMovementStep(movement, movement.rot);
                }
                else
                {
                    movement.targetspeed = 0;
                    movement.speed = -1;
                }
            }
        }

        public static void ApplyMovementStep(MoveMent movement, byte rotation)
        {
            double deltaX = Math.Sin(HeadingDegrees(rotation) * Math.PI / 180d) * movement.speed * -TickSeconds + movement.MovementRemainderX;
            double deltaZ = Math.Cos(HeadingDegrees(rotation) * Math.PI / 180d) * movement.speed * -TickSeconds + movement.MovementRemainderZ;
            int wholeX = (int)deltaX, wholeZ = (int)deltaZ;
            movement.globalPosition.X += wholeX;
            movement.globalPosition.Z += wholeZ;
            movement.MovementRemainderX = deltaX - wholeX;
            movement.MovementRemainderZ = deltaZ - wholeZ;
        }
    }
}

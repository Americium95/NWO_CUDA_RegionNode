namespace NWO_RegionNode
{
    public class MathR
    {
        public static int MoveTowardsAngle(int current, int target, int maxDelta)
        {
            // 각도를 0 ~ 360 사이로 변환
            current = ((current % 256) + 256) % 256;
            target = ((target % 256) + 256) % 256;

            // 두 각도 사이의 차이를 구함
            int delta = target - current;

            // 차이를 -180 ~ 180 사이로 변환
            if (delta > 128)
            {
                delta -= 256;
            }
            else if (delta < -128)
            {
                delta += 256;
            }

            // 차이의 절대값이 maxDelta보다 작거나 같으면 target으로 이동
            if (Math.Abs(delta) <= maxDelta)
            {
                return target;
            }

            // 그렇지 않으면 current에서 delta 방향으로 maxDelta만큼 이동
            return current + Math.Sign(delta) * maxDelta;
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta)
            {
                return target;
            }

            return current + Math.Sign(target - current) * maxDelta;
        }

    }
}

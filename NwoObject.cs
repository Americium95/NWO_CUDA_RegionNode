public class NwoObject
{
    public UInt32 id = 0;

    // 타일 좌표
    public Vector2 tilePosition = new Vector2();

    // 로컬 위치
    public Vector3 position = new Vector3();

    // 이동 속도
    public int speed = 0;

    // 회전
    public byte rot = 0;

    // 마지막 수신 시간
    public UInt16 receiveTime = 0;
}
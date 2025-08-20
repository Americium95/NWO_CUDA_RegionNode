//유저 데이터 클레스
using DotNetty.Transport.Channels;
using System.Numerics;

public class User
{
    public IChannelHandlerContext IChannel;
    public UInt32 scaffoldingIndex = 0;
    public int id = 0;
    public Vector3 position = new Vector3();
    public Vector2 tilePosition = new Vector2();
    public int speed = 0;
    public byte rot = 0;
    public UInt16 receiveTime=0;

    public User(IChannelHandlerContext context, int id, Vector2 tilePosition, Vector3 position, int spd, byte rot)
    {
        IChannel = context;
        this.id = id;
        this.tilePosition = tilePosition;
        this.position = position;
        this.speed = spd;
        this.rot = rot;
    }

}
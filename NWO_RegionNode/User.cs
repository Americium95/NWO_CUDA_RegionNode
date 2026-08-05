//유저 데이터 클레스
using DotNetty.Transport.Channels;
using System.Numerics;

public class User : NwoObject
{
    public IChannelHandlerContext IChannel;
    public UInt32 scaffoldingIndex = 0;

    public User(IChannelHandlerContext context, UInt32 id, Vector2 tilePosition, Vector3 position, int spd, byte rot)
    {
        IChannel = context;
        this.id = id;
        this.tilePosition = tilePosition;
        this.position = position;
        this.speed = spd;
        this.rot = rot;
        this.scaffoldingIndex = 0;
    }

}
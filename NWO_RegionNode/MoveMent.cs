using DotNetty.Transport.Channels;
using System.Numerics;

public class MoveMent
{
    public Int32 id = 0;
    public nwo_Vector3 position = new nwo_Vector3(0,0,0);
    public int speed = 0;
    public byte rot = 0;
    public UInt16 receiveTime = 0;

    public MoveMent(IChannelHandlerContext context, Int32 id, nwo_Vector3 position, int spd, byte rot)
    {
        this.id = id;
        this.position = position;
        this.speed = spd;
        this.rot = rot;
    }

    public class nwo_Vector3
    {
        public Int32 X = 0;
        public Int32 Y = 0;
        public Int32 Z = 0;

        public nwo_Vector3(Int32 X, Int32 Y,Int32 Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public static nwo_Vector3 operator +(nwo_Vector3 v0,nwo_Vector3 v1)
        {
            return new nwo_Vector3(v0.X+v1.X, v0.Y + v1.Y, v0.Z + v1.Z);
        }
    }
}
using System.Numerics;
using DotNetty.Transport.Channels;

public class MoveMent
{
    public UInt32 id = 0;
    public nwo_Vector3 globalPosition = new nwo_Vector3(0,0,0);
    public Vector3 position = new Vector3();
    public Vector2 tilePosition = new Vector2();
    public int targetspeed = 0;
    public float speed = 0;
    public byte targetAngle = 0;
    public byte Angle = 0;
    public UInt16 receiveTime = 0;

    public MoveMent(IChannelHandlerContext context, UInt32 id, nwo_Vector3 position, int spd, byte Angle)
    {
        this.id = id;
        this.globalPosition = position;
        this.tilePosition = new Vector2(position.X / 2560, position.Z / 2560);
        this.position = new Vector3(position.X / 2560, position.Y, position.Z / 2560);
        this.speed = spd;
        this.targetAngle = Angle;
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

        public static nwo_Vector3 operator *(nwo_Vector3 v, float f)
        {
            return new nwo_Vector3(
                (int)(v.X * f),
                (int)(v.Y * f),
                (int)(v.Z * f)
            );
        }
    }
}
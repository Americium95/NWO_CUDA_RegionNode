using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using NWO_RegionNode;
using System.Numerics;
using System.Text;

public class EchoServerHandler : ChannelHandlerAdapter
{
    //데이터 수신자
    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        var buffer = message as IByteBuffer;

        //Console.WriteLine("수신:" + buffer.GetByte(0) + "," + buffer.GetByte(1) + "," + buffer.GetByte(2) + "," + buffer.GetByte(3) + "," + buffer.GetByte(4) + "," + buffer.GetByte(5) + "," + buffer.GetByte(6) + "," + buffer.GetByte(7));

        //위치정보 동기화
        if (buffer.GetByte(0) == 2 && buffer.GetByte(1) == 1)
        {
            User Data;
            //유저 인덱스(구분자)
            int userIndex = BitConverter.ToInt16(new byte[] { buffer.GetByte(2), buffer.GetByte(3) }, 0);

            //발판 인덱스
            UInt32 scaffoldingIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(4), buffer.GetByte(5), buffer.GetByte(6), buffer.GetByte(7) }, 0);

            //타일 위치데이터 구성
            Vector2 tilePosition = new Vector2(
                BitConverter.ToInt16(new byte[] { buffer.GetByte(8), buffer.GetByte(9) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(10), buffer.GetByte(11) }));

            //위치데이터 구성
            Vector3 UserPosition = new Vector3(
                BitConverter.ToInt16(new byte[] { buffer.GetByte(12), buffer.GetByte(13) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(14), buffer.GetByte(15) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(16), buffer.GetByte(17) }));

            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(18), buffer.GetByte(19) });


            //각정보
            byte rot = buffer.GetByte(20);

            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(21), buffer.GetByte(22) });

            UInt16 delyTime = (ushort)(((UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond) - dataTime + 60000) % 60000);

            //Console.WriteLine(dataTime);

            //데이터 반영
            if (!Program.userTable.TryGetValue(userIndex, out Data))
            {
                Program.userTable.Add(userIndex, new User(context, userIndex, tilePosition, UserPosition, speed, rot));

                Data = Program.userTable[userIndex];
                Data.scaffoldingIndex = scaffoldingIndex;
                Data.position = UserPosition + new Vector3(MathF.Sin((float)rot * 1.4f * MathF.PI / 180), 0, MathF.Cos((float)rot * 1.4f * MathF.PI / 180)) * speed * delyTime / 1000;
                Data.speed = speed;
                Data.rot = rot;
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
            else
            {
                Data.IChannel = context;
                Data.scaffoldingIndex = scaffoldingIndex;
                Data.tilePosition = tilePosition;
                Data.position = UserPosition + new Vector3(MathF.Sin((float)rot * 1.4f * MathF.PI / 180), 0, MathF.Cos((float)rot * 1.4f * MathF.PI / 180)) * speed * delyTime / 1000;
                Data.speed = speed;
                Data.rot = rot;
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
        }

        //위치정보 관성항법 동기화
        if (buffer.GetByte(0) == 2 && buffer.GetByte(1) == 2)
        {
            User Data;
            //유저 인덱스
            int userIndex = BitConverter.ToInt16(new byte[] { buffer.GetByte(2), buffer.GetByte(3) }, 0);



            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(4), buffer.GetByte(5) });

            //각정보
            byte rot = buffer.GetByte(6);

            //y값 구성
            float Y = BitConverter.ToInt16(new byte[] { buffer.GetByte(7), buffer.GetByte(8) });

            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(9), buffer.GetByte(10) });

            UInt16 delyTime = (ushort)(((UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond) - dataTime + 60000) % 60000);

            //데이터 반영
            if (Program.userTable.TryGetValue(userIndex, out Data))
            {
                Data.IChannel = context;
                Data.position = Data.position + new Vector3(MathF.Sin((float)rot * 1.4f * MathF.PI / 180), 0, MathF.Cos((float)rot * 1.4f * MathF.PI / 180)) * speed * delyTime / 1000;
                Data.position.Y = Y;
                Data.speed = speed;
                Data.rot = rot;
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
        }

        //오브젝트 위치정보 동기화
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 1)
        {
            MoveMent Data;
            //오브젝트 인덱스
            Int32 moveMentIndex = BitConverter.ToInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);

            //위치데이터 구성
            MoveMent.nwo_Vector3 UserPosition = new MoveMent.nwo_Vector3(
                BitConverter.ToInt32(new byte[] { buffer.GetByte(6), buffer.GetByte(7), buffer.GetByte(8), buffer.GetByte(9) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(10), buffer.GetByte(11) }),
                BitConverter.ToInt32(new byte[] { buffer.GetByte(12), buffer.GetByte(13), buffer.GetByte(14), buffer.GetByte(15) }));

            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(16), buffer.GetByte(17) });

            //각정보
            byte Angle = buffer.GetByte(18);
              
            //지연시간 계산
            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(19), buffer.GetByte(20) });

            UInt16 delyTime = (ushort)(((UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond) - dataTime + 60000) % 60000);

            //Console.WriteLine(dataTime);

            //데이터 반영
            if (!Program.moveMentTable.TryGetValue(moveMentIndex, out Data))
            {
                Program.moveMentTable.Add(moveMentIndex, new MoveMent(context, moveMentIndex, UserPosition, speed, Angle));

                Data = Program.moveMentTable[moveMentIndex];
                Data.position = UserPosition;
                Data.speed = speed;
                Data.Angle = Angle;
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
            else
            {
                Data.position = UserPosition;
                Data.speed = speed;
                Data.targetAngle = Angle;
                Data.Angle = (byte)MathR.MoveTowardsAngle(Data.Angle, Angle, (int)(2 * MathF.Floor(delyTime / 500)));
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
        }

        //오브젝트 위치정보 관성항법 동기화
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 2)
        {
            MoveMent Data;
            //유저 인덱스
            Int32 moveMentIndex = BitConverter.ToInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);



            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(6), buffer.GetByte(7) });


            //각정보
            byte Angle = buffer.GetByte(8);

            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(9), buffer.GetByte(10) });

            UInt16 delyTime = (ushort)(((UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond) - dataTime + 60000) % 60000);

            //데이터 반영
            if (Program.moveMentTable.TryGetValue(moveMentIndex, out Data))
            {
                Data.position = Data.position;
                Data.speed = speed;
                Data.targetAngle = Angle;
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
        }

        //context.WriteAsync("aaaa");

        /*StringBuilder std = new StringBuilder();

        std.Append("UD{");

        foreach (var userData in Program.userTable)
        {
            std.Append(userData.Key);
            std.Append("{");
            std.Append(userData.Value.position.X);
            std.Append(",");
            std.Append(userData.Value.position.Y);
            std.Append(",");
            std.Append(userData.Value.position.Z);
            std.Append(",");
            std.Append(userData.Value.speed);
            std.Append(",");
            std.Append(userData.Value.rot);
            std.Append("}");
        }
        std.Append("}");

        Thread.Sleep(20);

        IByteBuffer buf = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(std.ToString()));*/

        //context.WriteAsync(buf);

        //Console.WriteLine("송신:"+buf.ToString(Encoding.UTF8));
    }

    public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        //Console.WriteLine("Exception: " + exception);
        context.CloseAsync();
    }

}
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using NWO_RegionNode;
using System.Diagnostics;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using static MoveMent;

public class EchoServerHandler : ChannelHandlerAdapter
{
    //데이터 수신자
    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        var buffer = message as IByteBuffer;

        //Console.WriteLine("수신:" + buffer.GetByte(0) + "," + buffer.GetByte(1) + "," + buffer.GetByte(2) + "," + buffer.GetByte(3) + "," + buffer.GetByte(4) + "," + buffer.GetByte(5) + "," + buffer.GetByte(6) + "," + buffer.GetByte(7));

        //위치정보 갱신요청
        if (buffer.GetByte(0) == 2 && buffer.GetByte(1) == 0)
        {
            User Data;

            //유저 인덱스(구분자)
            UInt32 userIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);

            Console.WriteLine(userIndex);

            //헤더 구성
            List<byte> packet = new List<byte> { 0x02, 0x00 };

            if (!Program.userTable.TryGetValue(userIndex, out Data))
            {
                Data = new User(context, userIndex, new Vector2(553, 394), new Vector3(-3443, 0, 160), 0, 0);
                Program.userTable.Add(userIndex, Data);
            }
            else
            {
                //Data = new User(context, userIndex, new Vector2(553, 394), new Vector3(-3443, 0, 160), 0, 0);
                if(Data.scaffoldingIndex>0)
                {
                    MoveMent scaffoldingObject = Program.moveMentTable[Data.scaffoldingIndex];
                    Data.tilePosition.X = (int)scaffoldingObject.globalPosition.X / 2560;
                    Data.tilePosition.Y = -(int)(scaffoldingObject.globalPosition.Z / 2560);
                    //Console.WriteLine(scaffoldingObject.position.X + ":"+ scaffoldingObject.position.Z);

                    Data.position.X += -(scaffoldingObject.globalPosition.X % 2560)*10;

                    Data.position.Z += -(scaffoldingObject.globalPosition.Z % 2560)*10;
                }
            }

            //발판 인덱스
            packet.AddRange(System.BitConverter.GetBytes((Int32)Data.scaffoldingIndex));

            //타일 위치데이터 구성
            packet.AddRange(System.BitConverter.GetBytes((Int16)Data.tilePosition.X));
            packet.AddRange(System.BitConverter.GetBytes((Int16)Data.tilePosition.Y));

            //Console.WriteLine(NetUserData.Value.tilePosition + "," + NetUserData.Value.position);

            //위치데이터 구성
            packet.AddRange(System.BitConverter.GetBytes((Int16)Data.position.X));
            packet.AddRange(System.BitConverter.GetBytes((Int16)Data.position.Y));
            packet.AddRange(System.BitConverter.GetBytes((Int16)Data.position.Z));

            //속도데이터 구성
            packet.AddRange(System.BitConverter.GetBytes((Int16)Data.speed));

            //각도 구성
            packet.Add(Data.rot);

            //전송
            context.WriteAndFlushAsync(Unpooled.CopiedBuffer(packet.ToArray()));
        }

        //위치정보 동기화
        if (buffer.GetByte(0) == 2 && buffer.GetByte(1) == 1)
        {
            User Data;
            //유저 인덱스(구분자)
            UInt32 userIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);

            //발판 인덱스
            UInt32 scaffoldingIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(6), buffer.GetByte(7), buffer.GetByte(8), buffer.GetByte(9) }, 0);

            //타일 위치데이터 구성
            Vector2 tilePosition = new Vector2(
                BitConverter.ToInt16(new byte[] { buffer.GetByte(10), buffer.GetByte(11) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(12), buffer.GetByte(13) }));

            //위치데이터 구성
            Vector3 UserPosition = new Vector3(
                BitConverter.ToInt16(new byte[] { buffer.GetByte(14), buffer.GetByte(15) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(16), buffer.GetByte(17) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(18), buffer.GetByte(19) }));

            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(20), buffer.GetByte(21) });


            //각정보
            byte rot = buffer.GetByte(22);

            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(23), buffer.GetByte(24) });

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
            UInt32 userIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);



            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(6), buffer.GetByte(7) });

            //각정보
            byte rot = buffer.GetByte(8);

            //y값 구성
            float Y = BitConverter.ToInt16(new byte[] { buffer.GetByte(9), buffer.GetByte(10) });

            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(11), buffer.GetByte(12) });

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
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 0)
        {
            MoveMent Data;

            //오브젝트 인덱스
            UInt32 moveMentIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);


            MoveMent.nwo_Vector3 UserPosition = new MoveMent.nwo_Vector3(
                BitConverter.ToInt32(new byte[] { buffer.GetByte(6), buffer.GetByte(7), buffer.GetByte(8), buffer.GetByte(9) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(10), buffer.GetByte(11) }),
                BitConverter.ToInt32(new byte[] { buffer.GetByte(12), buffer.GetByte(13), buffer.GetByte(14), buffer.GetByte(15) }));

            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(16), buffer.GetByte(17) });

            //각정보
            byte Angle = buffer.GetByte(18);

            //데이터 반영
            if (!Program.moveMentTable.TryGetValue(moveMentIndex, out Data))
            {
                Data = new MoveMent(context, moveMentIndex, UserPosition, speed, Angle);
                Program.moveMentTable.Add(moveMentIndex, Data);
            }

            //Data.position = UserPosition;
            Data.speed = speed;
            Data.targetspeed = speed;
        }

        //오브젝트 위치정보 동기화
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 1)
        {
            MoveMent Data;

            //오브젝트 인덱스
            UInt32 moveMentIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);

            MoveMent.nwo_Vector3 v = new MoveMent.nwo_Vector3(
                BitConverter.ToInt32(new byte[] { buffer.GetByte(6), buffer.GetByte(7), buffer.GetByte(8), buffer.GetByte(9) }),
                BitConverter.ToInt16(new byte[] { buffer.GetByte(10), buffer.GetByte(11) }),
                BitConverter.ToInt32(new byte[] { buffer.GetByte(12), buffer.GetByte(13), buffer.GetByte(14), buffer.GetByte(15) }));

            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(16), buffer.GetByte(17) });

            //각정보
            byte Angle = buffer.GetByte(18);

            //위치데이터 구성
            MoveMent.nwo_Vector3 UserPosition = v /*+ new MoveMent.nwo_Vector3((int)(MathF.Sin((float)Angle * 1.4f * MathF.PI / 180) * speed * -400 / 1000), 0, (int)(MathF.Cos((float)Angle * 1.4f * MathF.PI / 180) * speed * -400 / 1000))*/;
              
            //지연시간 계산
            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(19), buffer.GetByte(20) });

            UInt16 delyTime = (ushort)(((UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond) - dataTime + 60000) % 60000);

            //데이터 반영
            if (!Program.moveMentTable.TryGetValue(moveMentIndex, out Data))
            {
                Data = new MoveMent(context, moveMentIndex, UserPosition, speed, Angle);
                Program.moveMentTable.Add(moveMentIndex, Data);
            }
            
            
            Data.globalPosition = UserPosition;
            Data.targetspeed = speed;
            Data.targetAngle = Angle;
            //Data.Angle = Angle;
            //Data.Angle = (byte)MathR.MoveTowardsAngle(Data.Angle, Angle, (int)(2 * MathF.Floor(delyTime / 500)));
            Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
        }

        //오브젝트 위치정보 관성항법 동기화
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 2)
        {
            MoveMent Data;
            // 인덱스
            UInt32 moveMentIndex = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);

            //속도데이터 구성
            int speed = BitConverter.ToInt16(new byte[] { buffer.GetByte(6), buffer.GetByte(7) });

            //각정보
            byte Angle = buffer.GetByte(8);

            UInt16 dataTime = BitConverter.ToUInt16(new byte[] { buffer.GetByte(9), buffer.GetByte(10) });

            UInt16 delyTime = (ushort)(((UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond) - dataTime + 60000) % 60000);

            //데이터 반영
            if (Program.moveMentTable.TryGetValue(moveMentIndex, out Data))
            {
                Data.targetspeed = speed;
                Data.targetAngle = Angle;
                Data.receiveTime = (UInt16)(DateTime.Now.Second * 1000 + DateTime.Now.Millisecond);
            }
        }

        //Empty Packet
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 3)
        {

        }

        //공격
        if (buffer.GetByte(0) == 4 && buffer.GetByte(1) == 4)
        {
            UInt32 userNumber = BitConverter.ToUInt32(new byte[] { buffer.GetByte(2), buffer.GetByte(3), buffer.GetByte(4), buffer.GetByte(5) }, 0);
            byte bulletType = buffer.GetByte(6);
            Int32 startX = BitConverter.ToInt32(new byte[] { buffer.GetByte(7), buffer.GetByte(8), buffer.GetByte(9), buffer.GetByte(10) }, 0);
            Int16 startY = BitConverter.ToInt16(new byte[] { buffer.GetByte(11), buffer.GetByte(12) }, 0);
            Int32 startZ = BitConverter.ToInt32(new byte[] { buffer.GetByte(13), buffer.GetByte(14), buffer.GetByte(15), buffer.GetByte(16) }, 0);
            Int32 endX = BitConverter.ToInt32(new byte[] { buffer.GetByte(17), buffer.GetByte(18), buffer.GetByte(19), buffer.GetByte(20) }, 0);
            Int16 endY = BitConverter.ToInt16(new byte[] { buffer.GetByte(21), buffer.GetByte(22) }, 0);
            Int32 endZ = BitConverter.ToInt32(new byte[] { buffer.GetByte(23), buffer.GetByte(24), buffer.GetByte(25), buffer.GetByte(26) }, 0);
            Int16 heightRaw = BitConverter.ToInt16(new byte[] { buffer.GetByte(27), buffer.GetByte(28) }, 0);

            float height = heightRaw / 10f;
            Vector3 start = new Vector3(startX, startY / 10f, startZ);
            Vector3 end = new Vector3(endX, endY / 10f, endZ);

            foreach (KeyValuePair<UInt32, MoveMent> pair in Program.moveMentTable)
            {
                if (pair.Key == userNumber)
                    continue;

                nwo_Vector3 p = pair.Value.globalPosition;

                for (int i = 0; i <= 4; i++)
                {
                    float t = i * 0.25f;
                    float x = start.X + (end.X - start.X) * t;
                    float y = start.Y + (end.Y - start.Y) * t + height * 4f * t * (1f - t);
                    float z = start.Z + (end.Z - start.Z) * t;

                    float dx = p.X - x;
                    float dy = p.Y - y;
                    float dz = p.Z - z;

                    if (dx * dx + dy * dy + dz * dz <= 25f)
                    {
                        // 충돌 처리
                        break;
                    }
                }
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
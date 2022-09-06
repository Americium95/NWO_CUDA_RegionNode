using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using NWO_RegionNode;
using System.Numerics;
using System.Text;

public class EchoServerHandler : ChannelHandlerAdapter
{

    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        var buffer = message as IByteBuffer;

        string rcv = buffer.ToString(Encoding.UTF8).Substring(2);
        Console.WriteLine("수신:" + buffer.GetByte(0) + "," + buffer.GetByte(1) + "," + buffer.GetByte(2) + "," + buffer.GetByte(3) + "," + buffer.GetByte(4) + "," + buffer.GetByte(5) + "," + buffer.GetByte(6) + "," + buffer.GetByte(7));
        
        
        User Data;
        //유저 인덱스
        int userIndex = BitConverter.ToInt16(new byte[]{buffer.GetByte(0),buffer.GetByte(1)},0);

        //위치데이터 구성
        Data.position = new Vector3(
            0, 
            0, 
            0;
        //속도데이터 구성

        //각정보 구성


        //레거시 코드
        User Data;
        int userIndex = rcv.IndexOf("user");

        if (userIndex > -1)
        {
            string[] vetData = rcv.Split('{', '}')[1].Split(',');
            
            int userNum = int.Parse(rcv.Substring(userIndex + 4,rcv.IndexOf('{') - (userIndex + 5) ));

            if (!Program.userTable.TryGetValue(userNum, out Data))
            {
                Program.userTable.Add(userNum, new User(context,0, new Vector3(int.Parse(vetData[0]), int.Parse(vetData[1]), int.Parse(vetData[2])), int.Parse(vetData[3]), int.Parse(vetData[4])) );

                Data = Program.userTable[userNum];
            }
            else
            {
                Data.IChannel = context;
                Data.position = new Vector3(int.Parse(vetData[0]), int.Parse(vetData[1]), int.Parse(vetData[2]));
                Data.speed = int.Parse(vetData[3]);
                Data.rot = int.Parse(vetData[4]);
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
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
        Console.WriteLine("수신:" + rcv);




        User Data;
        int userIndex = rcv.IndexOf("user");
        string[] rcvData = rcv.Split(new char[] {',',':'});


        if (userIndex > -1)
        {
            if (!Program.userTable.TryGetValue(int.Parse(rcv[userIndex + 4].ToString()), out Data))
            {
                Program.userTable.Add(int.Parse(rcv[userIndex + 4].ToString()), new User(0, new Vector3(0, 0, 0), 0));

                Data = Program.userTable[int.Parse(rcv[userIndex + 4].ToString())];
            }
            else
            {
                Data.position = new Vector3(int.Parse(rcvData[1]), int.Parse(rcvData[2]), int.Parse(rcvData[3]));
            }
        }

        


        //context.WriteAsync("dtd");

        StringBuilder std = new StringBuilder();

        foreach (var userData in Program.userTable)
        {
            std.Append("{");
            std.Append(userData.Key);
            std.Append(",");
            std.Append(userData.Value.position.X);
            std.Append(",");
            std.Append(userData.Value.position.Y);
            std.Append(",");
            std.Append(userData.Value.position.Z);
            std.Append("}");
        }

        IByteBuffer buf = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(std.ToString()));

        context.WriteAsync(buf);

        //Console.WriteLine("송신:"+buf.ToString(Encoding.UTF8));
    }

    public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        //Console.WriteLine("Exception: " + exception);
        context.CloseAsync();
    }

}
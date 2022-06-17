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

        if (rcv.IndexOf("user") > -1)
        {
            if (!Program.userTable.TryGetValue(rcv[rcv.IndexOf("user") + 4], out Data))
            {
                Program.userTable.Add(rcv[rcv.IndexOf("user") + 4], new User(0, new Vector3(0, 0, 0), 0));

                Data = Program.userTable[rcv[rcv.IndexOf("user") + 4]];
            }
            else
            {
                Data.position = new Vector3(0, 0, 0);
            }
        }
        //context.WriteAsync("dtd");

        IByteBuffer buf = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes("out:"+Program.userTable.Count));

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
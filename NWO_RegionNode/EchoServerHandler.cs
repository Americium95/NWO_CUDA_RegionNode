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
        Console.WriteLine("수신:" + buffer.ToString(Encoding.UTF8));
        

        IByteBuffer buf = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes((buffer.ToString(Encoding.UTF8)).ToString()));

        User Data;

        if (!Program.userTable.TryGetValue(buffer.ToString(Encoding.UTF8)[5], out Data))
        {
            Program.userTable.Add(buffer.ToString(Encoding.UTF8)[5], new User(0, new Vector3(0, 0, 0), 0));

            Data = Program.userTable[buffer.ToString(Encoding.UTF8)[5]];
        }
        else
        {
            Data.position = new Vector3(0, 0, 0);
        }
        //context.WriteAsync("dtd");

        context.WriteAsync(buffer);

        //Console.WriteLine("송신:"+buf.ToString(Encoding.UTF8));
    }

    public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        //Console.WriteLine("Exception: " + exception);
        context.CloseAsync();
    }

}
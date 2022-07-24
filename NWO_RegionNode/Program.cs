using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using System.Text;
using System.Timers;

namespace NWO_RegionNode
{
    public class Program
    {
        static public Dictionary<int,User> userTable = new Dictionary<int,User>();


        static void Main(string[] args)
        {
            RunServerAsync();

            //락스텝 동기화 루프
            System.Timers.Timer LockStepTimer = new System.Timers.Timer(200);
            LockStepTimer.Elapsed += LockStep;
            LockStepTimer.AutoReset = true;
            LockStepTimer.Enabled =true;
            Console.ReadLine();
        }

        //락스텝 처리
        static void LockStep(Object source,ElapsedEventArgs e)
        {
            foreach (var NetuserData in Program.userTable)
            {
                StringBuilder std = new StringBuilder();

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

                IByteBuffer buf = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(std.ToString()));

                NetuserData.Value.IChannel.WriteAsync(buf);
            }
        }

        static async Task RunServerAsync()
        {

            var bossGroup = new MultithreadEventLoopGroup(1);
            var workerGroup = new MultithreadEventLoopGroup();

            try
            {
                var bootstrap = new ServerBootstrap();
                bootstrap
                    .Group(bossGroup, workerGroup)
                    .Channel<TcpServerSocketChannel>()
                    .Option(ChannelOption.SoBacklog, 100)
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
                    {
                        IChannelPipeline pipeline = channel.Pipeline;
                        pipeline.AddLast("framing-enc", new LengthFieldPrepender(2));

                        pipeline.AddLast("echo", new EchoServerHandler());
                    }));

                IChannel bootstrapChannel = await bootstrap.BindAsync(29100);

                while (true)
                {
                    Console.Write("\nNWO_RegionNode");
                    Console.Write("\n>>");
                    switch (Console.ReadLine())
                    {
                        case "stop":
                            Console.WriteLine("service close   (Y/N)");
                            if (Console.ReadLine().ToLower() == "y")
                            {
                                Console.WriteLine("Closing...");
                                await bootstrapChannel.CloseAsync();
                                return;
                            }
                            break;

                        default:
                            break;

                    }
                }


            }
            finally
            {
                await Task.WhenAll(
                    bossGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)),
                    workerGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
            }
        }


    }
}

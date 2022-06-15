using DotNetty.Codecs;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace NWO_RegionNode
{
    public class Program
    {
        static public List<User> userTable = new List<User>();


        static void Main(string[] args)
        {
            RunServerAsync();
            Console.ReadLine();
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

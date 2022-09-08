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


        //정밀동기화 주기 카운터
        int NetWorkRoutine=0;

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

            //위치정보 정밀 동기화
                if(NetWorkRoutine>4)
                {
                    foreach (var NetuserData in Program.userTable)
                    {
                        //헤더 구성
                        List<byte> packet = new List<byte> { 0x02, 0x01 };

                        //유저넘버 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetuserData.id));

                        //위치데이터 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetuserData.position.x));
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetuserData.position.y));
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetuserData.position.z));

                        //속도데이터 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetuserData.speed));

                        packet.Add(NetuserData.rot);

                        //전송
                        IByteBuffer buf = Unpooled.CopiedBuffer(packet.ToArray());

                        NetuserData.Value.IChannel.WriteAsync(buf);
                    }
                    //정밀동기화 주기 카운터 초기화
                    NetWorkRoutine=0;
                }else{
                    
                }
            NetWorkRoutine++;
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

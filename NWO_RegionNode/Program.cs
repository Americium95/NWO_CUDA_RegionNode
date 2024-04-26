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
        static int NetWorkRoutine=0;

        static void Main(string[] args)
        {
            RunServerAsync();

            //락스텝 동기화 루프
            System.Timers.Timer LockStepTimer = new System.Timers.Timer(50);
            LockStepTimer.Elapsed += LockStep;
            LockStepTimer.AutoReset = true;
            LockStepTimer.Enabled =true;
            Console.ReadLine();
        }

        //락스텝 처리
        static void LockStep(Object source,ElapsedEventArgs e)
        {

            //위치정보 정밀 동기화

            /*
            *최소주기 기준인0.5s로 설정됨
            */

            if(NetWorkRoutine>5)
            {
                //cuda로 데이터 적재


                foreach (var broadcastUserData in Program.userTable)
                {
                    int DataCount=0;

                    //헤더 구성
                    List<byte> packet = new List<byte> { 0x02, 0x01 };

                    //유저id 등록
                    packet.AddRange(System.BitConverter.GetBytes((Int16)broadcastUserData.Value.id));

                    //cuda연산


                    
                    //브로드케스트
                    foreach (var NetUserData in Program.userTable)
                    {
                        //본인 제외
                        //if(NetUserData.Key!=broadcastUserData.Key)
                        {
                            //유저넘버 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Key));

                            //타일 위치데이터 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.tilePosition.X));
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.tilePosition.Y));

                            //위치데이터 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.position.X));
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.position.Y));
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.position.Z));

                            //속도데이터 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.speed));

                            //각도 구성
                            packet.Add(NetUserData.Value.rot);
                            DataCount++;
                        }
                    }

                    //데이터 개수를 보냄
                    packet.InsertRange(4,System.BitConverter.GetBytes((Int16)DataCount));

                    //송신
                    broadcastUserData.Value.IChannel.WriteAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                }
                //정밀동기화 주기 카운터 초기화
                NetWorkRoutine=0;
            //위치정보 근사 동기화
            }else{
                foreach (var broadcastUserData in Program.userTable)
                {
                    int DataCount=0;

                    //헤더 구성
                    List<byte> packet = new List<byte> { 0x02, 0x02 };

                    //유저id 등록
                    packet.AddRange(System.BitConverter.GetBytes((Int16)broadcastUserData.Key));
                    
                    foreach (var NetUserData in Program.userTable)
                    {
                        //본인 제외
                        //if(NetUserData.Key!=broadcastUserData.Key)
                        {

                        //유저넘버 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Key));

                        //속도데이터 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.speed));

                        //각도 구성
                        packet.Add(NetUserData.Value.rot);



                        Console.WriteLine(NetUserData.Value.position);
                        }
                    }


                    //데이터 개수를 보냄
                    packet.InsertRange(4,System.BitConverter.GetBytes((Int16)DataCount));

                    //브로드케스트
                    broadcastUserData.Value.IChannel.WriteAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                }
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

                        pipeline.AddLast("echo", new EchoServerHandler());
                    }));

                IChannel bootstrapChannel = await bootstrap.BindAsync(29001);

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

using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using System.Runtime.InteropServices;
using System.Timers;

namespace NWO_RegionNode
{
    public class Program
    {
        // addWithCuda 함수 선언
        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int exportCppFunctionAdd(float4[] dst, float4 start, float4[] src, uint arraySize);

        static public Dictionary<int, User> userTable = new Dictionary<int, User>();

        //근사동기화 주기 카운터
        static int NetWorkRoutine = 0;

        static void Main(string[] args)
        {
            const int arraySize = 5;

            float4[] a = new float4[arraySize]
            {
                new float4(1, 2, 3, 0),
                new float4(4, 5, 6, 0),
                new float4(7, 8, 9, 0),
                new float4(10, 11, 12, 0),
                new float4(13, 14, 15, 0)
            };

            float4[] b = new float4[arraySize]
            {
            new float4(15, 14, 13, 0),
            new float4(12, 11, 10, 0),
            new float4(9, 8, 7, 0),
            new float4(6, 5, 4, 0),
            new float4(3, 2, 1, 0)
            };

            float4[] c = new float4[arraySize];


            // C#에서 CUDA 함수 호출
            int cudaStatus = exportCppFunctionAdd(c, b[0], a, (uint)arraySize);
            /*if (cudaStatus != cudaError_t.Success)
            {
                Console.WriteLine("addWithCuda failed!");
                return;
            }*/
            // 결과 출력
            for (int i = 0; i < arraySize; ++i)
            {
                Console.WriteLine($"{i}: {{ {a[i].x}, {a[i].y}, {a[i].z} }} + {{ {b[i].x}, {b[i].y}, {b[i].z} }} = {{ {c[i].x}, {c[i].y}, {c[i].z}, {c[i].w}  }}");
            }


            //비동기서버 시작
            RunServerAsync();

            //락스텝 동기화 루프
            System.Timers.Timer LockStepTimer = new System.Timers.Timer(150);
            LockStepTimer.Elapsed += LockStep;
            LockStepTimer.AutoReset = true;
            LockStepTimer.Enabled = true;
            Console.ReadLine();
        }

        //락스텝 처리
        static void LockStep(Object source, ElapsedEventArgs e)
        {

            //위치정보 동기화

            /*
            *최소주기 기준인0.5s로 설정됨
            */

            if (NetWorkRoutine > 2)
            {
                //cuda로 데이터 적재


                foreach (var broadcastUserData in Program.userTable)
                {
                    int DataCount = 0;

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
                            //거리 비교
                            //if (DistanceSquared(NetUserData.Value.tilePosition, NetUserData.Value.tilePosition) < 2 && DistanceSquared(NetUserData.Value.position) < 200)

                            //유저넘버 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Key));

                            //타일 위치데이터 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.tilePosition.X));
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.tilePosition.Y));


                            Console.WriteLine(NetUserData.Value.tilePosition + "," + NetUserData.Value.position);

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
                    packet.InsertRange(4, System.BitConverter.GetBytes((Int16)DataCount));

                    //송신
                    broadcastUserData.Value.IChannel.WriteAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                }
                //정밀동기화 주기 카운터 초기화
                NetWorkRoutine = 0;
            }
            //위치정보 근사 동기화
            else
            {
                foreach (var broadcastUserData in Program.userTable)
                {
                    int DataCount = 0;

                    //헤더 구성
                    List<byte> packet = new List<byte> { 0x02, 0x02 };

                    //유저id 등록
                    packet.AddRange(System.BitConverter.GetBytes((Int16)broadcastUserData.Value.id));

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
                            DataCount++;
                        }
                    }


                    //데이터 개수를 보냄
                    packet.InsertRange(4, System.BitConverter.GetBytes((Int16)DataCount));

                    //송신
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

        // float4 구조체 정의
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct float4
        {
            public float x, y, z, w;

            public float4(float x, float y, float z, float w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }
        }

    }
}

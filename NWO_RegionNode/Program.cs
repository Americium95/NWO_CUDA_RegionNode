using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using NWO_RegionNode.PathValidation;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Timers;

namespace NWO_RegionNode
{

    public class Program
    {
        public const int MAX_USER = 65536;
        public const int MAX_OBJECT = 65536;

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int cudaUserMemCopy(
            float4[] a,
            int arraySize);

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr cudaUserDisFilter(
            float[] dst,
            float4 start,
            int arraySize);

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int cudaUserMemFree();


        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int cudaMoveMentMemCopy(
            float4[] a,
            int arraySize);

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr cudaMoveMentDisFilter(
            float[] dst,
            float4 start,
            int arraySize);

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int cudaMoveMentMemFree();


        static public Dictionary<UInt32, User> userTable = new Dictionary<UInt32, User>();
        static public Dictionary<UInt32, MoveMent> moveMentTable = new Dictionary<UInt32, MoveMent>();
        static public AutopilotController? Autopilot { get; set; }

        float4[] userPositionBuffer = new float4[MAX_USER];
        float4[] objectPositionBuffer = new float4[MAX_OBJECT];

        //관성항법 동기화 주기 카운터
        static int NetWorkRoutine = 0;
        static int movementTickInProgress = 0;


        static void Main(string[] args)
        {
            const int arraySize = 5;

            float4[] a = new float4[arraySize]
            {
                new float4(1, 3, 3, 0),
                new float4(1, 2, 6, 0),
                new float4(1, 2, 9, 0),
                new float4(1, 2, 12, 0),
                new float4(1, 2, 15, 0)
            };

            float4[] b = new float4[arraySize]
            {
            new float4(15, 14, 5, 0),
            new float4(1, 2, 5, 0),
            new float4(9, 8, 7, 0),
            new float4(6, 5, 4, 0),
            new float4(3, 2, 1, 0)
            };

            float[] c = new float[arraySize];


            //vram등록
            cudaUserMemCopy(a, arraySize);
            //연산,결과
            IntPtr resultPtr = cudaUserDisFilter(c, b[1], arraySize);
            //resultPtr = exportCppFunctionAdd(c, b[1], arraySize);

            //메모리 해제
            cudaUserMemFree();

            //Console.WriteLine(cudaStatus);
            /*if (cudaStatus != cudaError_t.Success)
            {
                Console.WriteLine("addWithCuda failed!");
                return;
            }*/
            // 결과 출력
            for (int i = 0; i < arraySize; ++i)
            {
                Console.WriteLine($"{i}: {{ {a[i].x}, {a[i].y}, {a[i].z} }} , {{ {b[1].x}, {b[1].y}, {b[1].z} }} = {{ {c[i]} }}");
            }


            //비동기서버 시작
            RunServerAsync();

            //유저락스텝 동기화 루프
            System.Timers.Timer userLockStepTimer = new System.Timers.Timer(150);
            userLockStepTimer.Elapsed += userLockStep;
            userLockStepTimer.AutoReset = true;
            userLockStepTimer.Enabled = true;

            //오브젝트 락스텝 동기화 루프
            System.Timers.Timer moveMentLockStepTimer = new System.Timers.Timer(LockstepMovement.TickMilliseconds);
            moveMentLockStepTimer.Elapsed += moveMentLockStep;
            moveMentLockStepTimer.AutoReset = true;
            moveMentLockStepTimer.Enabled = true;


            Console.ReadLine();
        }

        //플레이어 락스텝 처리
        static void userLockStep(Object source, ElapsedEventArgs e)
        {

            //위치 동기화
            if (NetWorkRoutine > 2)
            {
                //cuda로 데이터 적재

                try
                {
                    //vram등록
                    cudaUserMemCopy(userTable.Select(o => new float4(o.Value.tilePosition.X, o.Value.tilePosition.Y, o.Value.position.X, o.Value.position.Z)).ToArray(), userTable.Count);
                    //결과버퍼
                    float[] c = new float[userTable.Count];

                    foreach (var broadcastUserData in Program.userTable)
                    {
                        int DataCount = 0;

                        //헤더 구성
                        List<byte> packet = new List<byte> { 0x02, 0x01 };

                        //수신 유저id 등록
                        packet.AddRange(System.BitConverter.GetBytes((UInt32)broadcastUserData.Value.id));



                        //cpu 브로드케스트
                        /*foreach (var NetUserData in Program.userTable)
                        {
                            //본인 제외
                            //if(NetUserData.Key!=broadcastUserData.Key)
                            if (false)
                            {
                                //거리 비교
                                //if (DistanceSquared(NetUserData.Value.tilePosition, NetUserData.Value.tilePosition) < 2 && DistanceSquared(NetUserData.Value.position) < 200)

                                //유저넘버 구성
                                packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Key));

                                //타일 위치데이터 구성
                                packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.tilePosition.X));
                                packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.tilePosition.Y));


                                //Console.WriteLine(NetUserData.Value.tilePosition + "," + NetUserData.Value.position);

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
                        }*/

                        //cuda 연산 결과
                        IntPtr resultPtr = cudaUserDisFilter(c, new float4(broadcastUserData.Value.tilePosition.X, broadcastUserData.Value.tilePosition.Y, broadcastUserData.Value.position.X, broadcastUserData.Value.position.Z), userTable.Count);


                        int i = 0;

                        //유저 데이터로부터 페킷 생성
                        foreach (KeyValuePair<UInt32, User> userData in userTable)
                        {
                            //거리필터
                            //if (c[i] < 2000)
                            {
                                //유저넘버 구성
                                packet.AddRange(System.BitConverter.GetBytes((UInt32)userData.Value.id));
                               // Console.WriteLine(userData.Value.id);

                                packet.AddRange(System.BitConverter.GetBytes((UInt32)userData.Value.scaffoldingIndex));

                                //타일 위치데이터 구성
                                packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Value.tilePosition.X));
                                packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Value.tilePosition.Y));

                                //Console.WriteLine(NetUserData.Value.tilePosition + "," + NetUserData.Value.position);

                                //위치데이터 구성
                                packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Value.position.X));
                                packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Value.position.Y));
                                packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Value.position.Z));

                                //속도데이터 구성
                                packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Value.speed));

                                //각도 구성
                                packet.Add(userData.Value.rot);

                                //수신시간 추가
                                packet.AddRange(System.BitConverter.GetBytes((UInt16)userData.Value.receiveTime));

                                DataCount++;
                            }

                            i++;
                        }

                        //데이터 개수
                        packet.InsertRange(6, System.BitConverter.GetBytes((Int16)DataCount));

                        //송신
                        broadcastUserData.Value.IChannel.WriteAndFlushAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                    }
                    //동기화 주기 카운터 초기화
                    NetWorkRoutine = 0;
                }
                finally
                {
                    //vram 해제
                    cudaUserMemFree();
                }
            }
            //관성항법 동기화
            else
            {
                foreach (var broadcastUserData in Program.userTable)
                {
                    int DataCount = 0;

                    //헤더 구성
                    List<byte> packet = new List<byte> { 0x02, 0x02 };

                    //유저id 등록
                    packet.AddRange(System.BitConverter.GetBytes((UInt32)broadcastUserData.Value.id));

                    foreach (var NetUserData in Program.userTable)
                    {
                        //본인 제외
                        //if(NetUserData.Key!=broadcastUserData.Key)
                        {

                            //유저넘버 구성
                            packet.AddRange(System.BitConverter.GetBytes((UInt32)NetUserData.Value.id));


                            //속도데이터 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.speed));
                            //각도 구성
                            packet.Add(NetUserData.Value.rot);
                            //y값 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)NetUserData.Value.position.Y));

                            //수신시간 추가
                            packet.AddRange(System.BitConverter.GetBytes((UInt16)NetUserData.Value.receiveTime));

                            DataCount++;
                        }
                    }


                    //데이터 개수를 보냄
                    packet.InsertRange(6, System.BitConverter.GetBytes((Int16)DataCount));

                    //송신
                    broadcastUserData.Value.IChannel.WriteAndFlushAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                }
            }
            NetWorkRoutine++;

        }

        //오브젝트 락스텝 처리
        static void moveMentLockStep(Object source, ElapsedEventArgs e)
        {
            if (Interlocked.Exchange(ref movementTickInProgress, 1) != 0) return;
            try
            {
                try
                {
                    cudaMoveMentMemCopy(moveMentTable.Select(o => new float4(o.Value.tilePosition.X, o.Value.tilePosition.Y, o.Value.position.X, o.Value.position.Z)).ToArray(), moveMentTable.Count);
                    float[] c = new float[moveMentTable.Count];
                    if (moveMentTable.Count == 0)
                        return;
                    foreach (var broadcastUserData in Program.userTable)
                    {
                        cudaMoveMentDisFilter(c, new float4(broadcastUserData.Value.tilePosition.X, broadcastUserData.Value.tilePosition.Y, broadcastUserData.Value.position.X, broadcastUserData.Value.position.Z), moveMentTable.Count);
                    }

                    AdvanceAndBroadcastMovement(
                        Autopilot,
                        moveMentTable.Values,
                        Terrain.terrainCollision,
                        userTable.Values,
                        SendMovementSnapshot);
                }
                finally
                {
                    cudaMoveMentMemFree();
                }
            }
            finally
            {
                Interlocked.Exchange(ref movementTickInProgress, 0);
            }
        }

        /// <summary>Advances authoritative movement and broadcasts its production snapshots to each recipient.</summary>
        public static void AdvanceAndBroadcastMovement(
            AutopilotController? autopilot,
            IEnumerable<MoveMent> movements,
            Func<int, int, float> terrainHeight,
            IEnumerable<User> recipients,
            Action<IChannelHandlerContext, byte[]> send)
        {
            if (movements == null) throw new ArgumentNullException(nameof(movements));
            if (terrainHeight == null) throw new ArgumentNullException(nameof(terrainHeight));
            if (recipients == null) throw new ArgumentNullException(nameof(recipients));
            if (send == null) throw new ArgumentNullException(nameof(send));

            MoveMent[] movementSnapshot = movements.ToArray();
            LockstepMovement.AdvanceTick(autopilot, movementSnapshot, terrainHeight);
            foreach (User recipient in recipients)
            {
                if (recipient?.IChannel?.Channel == null || movementSnapshot.Length == 0) continue;
                send(recipient.IChannel, SerializeMovementSnapshot(recipient.id, movementSnapshot));
            }
        }

        /// <summary>Serializes one 0x04/0x01 production object snapshot for a recipient.</summary>
        public static byte[] SerializeMovementSnapshot(uint recipientId, IReadOnlyCollection<MoveMent> movements)
        {
            if (movements == null) throw new ArgumentNullException(nameof(movements));

            List<byte> packet = new List<byte> { 0x04, 0x01 };
            packet.AddRange(BitConverter.GetBytes(recipientId));
            packet.InsertRange(6, BitConverter.GetBytes((short)movements.Count));
            foreach (MoveMent movement in movements)
            {
                packet.AddRange(BitConverter.GetBytes(unchecked((int)movement.id)));
                packet.AddRange(BitConverter.GetBytes(movement.globalPosition.X));
                packet.AddRange(BitConverter.GetBytes(unchecked((short)movement.globalPosition.Y)));
                packet.AddRange(BitConverter.GetBytes(movement.globalPosition.Z));
                packet.AddRange(BitConverter.GetBytes(unchecked((short)movement.speed)));
                packet.Add(movement.rot);
            }
            return packet.ToArray();
        }

        public static void SendMovementSnapshot(IChannelHandlerContext recipient, byte[] packet)
        {
            recipient.WriteAndFlushAsync(Unpooled.CopiedBuffer(packet));
        }

        static async Task RunServerAsync()
        {

            var navMeshResolution = InitializeNavigation();
            Console.WriteLine($"[navigation] provider={navMeshResolution.Source}, failClosed={navMeshResolution.FailClosed}, detail={navMeshResolution.Detail}");

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

                        ConfigureClientPipeline(pipeline, navMeshResolution.Provider, Autopilot!);
                    }));

                IChannel bootstrapChannel = await bootstrap.BindAsync(19100);

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
            catch (Exception ex)
            {
                Console.WriteLine("예외가 발생했습니다.");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

                // 또는
                // Console.WriteLine(ex);
            }
            finally
            {
                await Task.WhenAll(
                    bossGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)),
                    workerGroup.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
            }
        }

        /// <summary>Builds the production inbound pipeline; kept public for startup-path tests.</summary>
        public static void ConfigureClientPipeline(IChannelPipeline pipeline, IAuthoritativeNavMeshProvider provider, AutopilotController autopilot)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (autopilot == null) throw new ArgumentNullException(nameof(autopilot));
            pipeline.AddLast("path-frame-decoder", new PathFrameDecoder());
            pipeline.AddLast("navigation", new NavigationFrameHandler(provider, autopilot));
            pipeline.AddLast("echo", new EchoServerHandler());
        }

        /// <summary>Resolves configured authoritative navigation and initializes the singleton controller.</summary>
        public static NavMeshProviderResolution InitializeNavigation(string? baseDirectory = null)
        {
            NavMeshProviderResolution resolution = NavMeshProviderFactory.Resolve(baseDirectory ?? AppContext.BaseDirectory);
            Autopilot = new AutopilotController(resolution.Provider);
            return resolution;
        }

        // float4 구조체 정의
        [StructLayout(LayoutKind.Sequential)]
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

            public float4(Vector3 i)
            {
                this.x = i.X;
                this.y = i.Y;
                this.z = i.Z;
                this.w = 0;
            }
        }

    }
}

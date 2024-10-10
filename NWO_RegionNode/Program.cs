using DotNetty.Buffers;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Timers;
using static NWO_RegionNode.Program;

namespace NWO_RegionNode
{
    public class Program
    {
        // Cuda 함수 선언
        // addWithCuda 함수 선언
        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe public static extern int cudaMemCopy(float4[] a, int arraySize);

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe public static extern IntPtr exportCppFunctionAdd(float[] dst, float4 start, int arraySize);

        [DllImport("CudaRuntime1.dll", CallingConvention = CallingConvention.Cdecl)]
        unsafe public static extern int cudaMemFree();


        static public Dictionary<int, User> userTable = new Dictionary<int, User>();
        static public Dictionary<Int32, MoveMent> moveMentTable = new Dictionary<Int32, MoveMent>();

        //근사동기화 주기 카운터
        static int NetWorkRoutine = 0;

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
            cudaMemCopy(a, arraySize);
            //연산,결과
            IntPtr resultPtr = exportCppFunctionAdd(c, b[1], arraySize);
            //resultPtr = exportCppFunctionAdd(c, b[1], arraySize);

            for (int i = 0; i < arraySize; i++)
            {
                IntPtr currentPtr = IntPtr.Add(resultPtr, i * Marshal.SizeOf(typeof(float)));
                c[i] = Marshal.PtrToStructure<float>(currentPtr);
            }
            //메모리 해제
            cudaMemFree();

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
            System.Timers.Timer moveMentLockStepTimer = new System.Timers.Timer(500);
            moveMentLockStepTimer.Elapsed += moveMentLockStep;
            moveMentLockStepTimer.AutoReset = true;
            moveMentLockStepTimer.Enabled = true;

            Console.ReadLine();
        }

        //플레이어 락스텝 처리
        static void userLockStep(Object source, ElapsedEventArgs e)
        {

            //위치정보 동기화

            /*
            *최소주기 기준인0.5s로 설정됨
            */

            if (NetWorkRoutine > 2)
            {
                //cuda로 데이터 적재

                //vram등록
                cudaMemCopy(userTable.Select(o => new float4(o.Value.tilePosition.X,o.Value.tilePosition.Y,o.Value.position.X,o.Value.position.Z)).ToArray(), userTable.Count);
                //결과버퍼
                float[] c = new float[userTable.Count];

                foreach (var broadcastUserData in Program.userTable)
                {
                    int DataCount = 0;

                    //헤더 구성
                    List<byte> packet = new List<byte> { 0x02, 0x01 };

                    //수신 유저id 등록
                    packet.AddRange(System.BitConverter.GetBytes((Int16)broadcastUserData.Value.id));



                    //cpu 브로드케스트
                    foreach (var NetUserData in Program.userTable)
                    {
                        //본인 제외
                        //if(NetUserData.Key!=broadcastUserData.Key)
                        if(false)
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
                    }

                    //cuda브로드케스트
                    //cuda 연산,결과
                    IntPtr resultPtr = exportCppFunctionAdd(c, new float4(broadcastUserData.Value.tilePosition.X, broadcastUserData.Value.tilePosition.Y, broadcastUserData.Value.position.X, broadcastUserData.Value.position.Z), userTable.Count);


                    int i = 0;

                    //유저 데이터로부터 페킷 생성
                    foreach (KeyValuePair<int,User> userData in userTable)
                    {
                        IntPtr currentPtr = IntPtr.Add(resultPtr, i * Marshal.SizeOf(typeof(float)));
                        c[i] = Marshal.PtrToStructure<float>(currentPtr);

                        //거리필터
                        //if (c[i] < 2000)
                        {
                            //유저넘버 구성
                            packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Key));

                            packet.AddRange(System.BitConverter.GetBytes((UInt16)userData.Value.scaffoldingIndex));

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

                    //유저 데이터 개수를 보냄
                    packet.InsertRange(4, System.BitConverter.GetBytes((Int16)DataCount));

                    //송신
                    broadcastUserData.Value.IChannel.WriteAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                }
                //정밀동기화 주기 카운터 초기화
                NetWorkRoutine = 0;

                //vram 해제
                cudaMemFree();
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

                            //수신시간 추가
                            packet.AddRange(System.BitConverter.GetBytes((UInt16)NetUserData.Value.receiveTime));

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

        //오브젝트 락스텝 처리
        static void moveMentLockStep(Object source, ElapsedEventArgs e)
        {
            //이동처리
            foreach (var NetMoveMentData in Program.moveMentTable)
            {
                NetMoveMentData.Value.position += new MoveMent.nwo_Vector3((int)(MathF.Sin((float)NetMoveMentData.Value.rot * 1.4f * MathF.PI / 180) * NetMoveMentData.Value.speed * -500 / 1000), 0, (int)(MathF.Cos((float)NetMoveMentData.Value.rot * 1.4f * MathF.PI / 180) * NetMoveMentData.Value.speed * -500 / 1000));

                //NetMoveMentData.Value.tilePosition.X += (int)(NetMoveMentData.Value.position.X / 2560);
                //NetMoveMentData.Value.tilePosition.Y += (int)(NetMoveMentData.Value.position.Z / 2560);

                //NetMoveMentData.Value.position.X = (int)(NetMoveMentData.Value.position.X % 2560);
                //NetMoveMentData.Value.position.Z = (int)(NetMoveMentData.Value.position.Z % 2560);
            }


            //위치정보 동기화

            /*
            *최소주기 기준인0.5s로 설정됨
            */

            //cuda로 데이터 적재

            //vram등록
            //cudaMemCopy(userTable.Select(o => new float4(o.Value.tilePosition.X, o.Value.tilePosition.Y, o.Value.position.X, o.Value.position.Z)).ToArray(), userTable.Count);
            //결과버퍼
            float[] c = new float[userTable.Count];

            foreach (var broadcastUserData in Program.userTable)
            {
                int DataCount = 0;

                //헤더 구성
                List<byte> packet = new List<byte> { 0x04, 0x01 };

                //수신 유저id 등록
                packet.AddRange(System.BitConverter.GetBytes((Int16)broadcastUserData.Value.id));



                //cpu 브로드케스트
                foreach (var NetMoveMentData in Program.moveMentTable)
                {
                    //본인 제외
                    //if(NetUserData.Key!=broadcastUserData.Key)
                    //if (false)
                    {
                        //거리 비교
                        //if (DistanceSquared(NetUserData.Value.tilePosition, NetUserData.Value.tilePosition) < 2 && DistanceSquared(NetUserData.Value.position) < 200)

                        //유저넘버 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int32)NetMoveMentData.Key));
                        Console.WriteLine((Int32)NetMoveMentData.Value.position.X);


                        //Console.WriteLine(NetUserData.Value.tilePosition + "," + NetUserData.Value.position);

                        //위치데이터 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int32)NetMoveMentData.Value.position.X));
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetMoveMentData.Value.position.Y));
                        packet.AddRange(System.BitConverter.GetBytes((Int32)NetMoveMentData.Value.position.Z));

                        //속도데이터 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)NetMoveMentData.Value.speed));

                        //각도 구성
                        packet.Add(NetMoveMentData.Value.rot);


                        DataCount++;
                    }
                }

                //cuda브로드케스트
                //cuda 연산,결과
                //IntPtr resultPtr = exportCppFunctionAdd(c, new float4(broadcastUserData.Value.tilePosition.X, broadcastUserData.Value.tilePosition.Y, broadcastUserData.Value.position.X, broadcastUserData.Value.position.Z), userTable.Count);


                int i = 0;

                //유저 데이터로부터 페킷 생성
                foreach (KeyValuePair<int, User> userData in userTable)
                {
                    break;
                    //IntPtr currentPtr = IntPtr.Add(resultPtr, i * Marshal.SizeOf(typeof(float)));
                    //c[i] = Marshal.PtrToStructure<float>(currentPtr);

                    //거리필터
                    //if (c[i] < 2000)
                    {
                        //유저넘버 구성
                        packet.AddRange(System.BitConverter.GetBytes((Int16)userData.Key));

                        packet.AddRange(System.BitConverter.GetBytes((UInt16)userData.Value.scaffoldingIndex));

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

                //오브젝트 데이터 개수를 보냄
                packet.InsertRange(4, System.BitConverter.GetBytes((Int16)DataCount));

                if (DataCount > 0)
                {
                    //송신
                    broadcastUserData.Value.IChannel.WriteAsync(Unpooled.CopiedBuffer(packet.ToArray()));
                }
            }

            //vram 해제
            //cudaMemFree();
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

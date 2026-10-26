import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace sections 8.2 and 8.3 with the new unified design
old_section = '''<h3>8.2 선택지 A (권장) — 0x06 전용 리스너 포트</h3>
<p><b>기존 19100 파이프라인을 한 줄도 바꾸지 않고</b> 19101 에 0x06 전용 파이프라인을 만든다. 이 선택지가 <b>유일하게 기존 동작에 위험을 추가하지 않는다.</b></p>
<pre>// Program.cs RunServerAsync 에 추가할 두 번째 bootstrap (제안, 미적용)
// 기존 19100 bootstrap 은 그대로 두고, 19101 만 0x06 전용으로 만든다.
var pathPlanBoss   = new MultithreadEventLoopGroup(1);
var pathPlanWorker = new MultithreadEventLoopGroup();
var pathPlanBootstrap = new ServerBootstrap();
pathPlanBootstrap
    .Group(pathPlanBoss, pathPlanWorker)
    .Channel<TcpServerSocketChannel>()
    .Option(ChannelOption.SoBacklog, 100)
    .Option(ChannelOption.TcpNodelay, true)
    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
    {
        IChannelPipeline pipeline = channel.Pipeline;
        pipeline.AddLast("pathplan.frame", new PathPlanFrameDecoder());          // 동기, await 금지
        pipeline.AddLast("pathplan.handler", new PathPlanValidationHandler(CreateValidationContext));
        // EchoServerHandler 는 이 파이프라인에 넣지 않는다.
    }));
IChannel pathPlanChannel = await pathPlanBootstrap.BindAsync(19101);
// PathPlanValidationContext 팩토리: 서버 navMeshVersion/navMesh, 서버 시계, 선박별 LastObservedPlanSequence,
// RequestsInCurrentWindow, AuthoritativeShipPolygon, IAuthoritativeNavMesh 구현체를 여기서 주입한다.
// 이 리스너의 인증은 19100 세션과 별개로 建立되므로, <b>인증 세션 없이는 요청을 받아들이지 않는다</b>
// (연결 시 토큰/사용자 ID 핸드셰이크를 추가하는 것은 별도 과제).</pre>
<h3>8.3 선택지 B — 같은 포트 혼합 디스패처 (조건부, 비권장)</h3>
<p>같은 포트가 불가피한 경우에만 검토한다. 아래는 <b>안전 규칙 전체</b>이며, 마지막 항목 때문에 기본값이 아니다.</p>
<pre>// AI_Work/PathProtocol/MixedProtocolDispatcher.cs   (제안, 미생성)
// "echo" 핸들러 <b>앞</b>에 놓는다. 두 프로토콜을 한 누적 버퍼에서 순서대로 잘라내는 유일한 방법.
protected override void Decode(IChannelHandlerContext ctx, IByteBuffer input, List<object> output)
{
    for (;;)
    {
        // (0) ★ 최소 1바이트: TCP 는 메시지 경계를 보장하지 않는다. opcode 바이트조차 없을 수 있다.
        if (input.ReadableBytes() < 1) return;
        byte op = input.GetByte(input.ReaderIndex);        // ★ peek. readerIndex 를 움직이지 않는다.

        if (op == PathPlanContract.OpcodePathPlan)
        {
            // ── 0x06 경로: 자기경계 헤더 ──────────────────────────────
            if (input.ReadableBytes() < PathPlanContract.HeaderLength) return;   // 헤더 대기
            if (!PathPlanHeaderCodec.TryRead(input.GetReaderSlice(), out var h, out var err)) return EmitFrameError(ctx, err);

            // 2) 상한 검증 — 메모리를 제한하는 유일한 지점. 할당보다 먼저.
            if (h.TotalPacketLength < PathPlanContract.MinPacketLength || h.TotalPacketLength > PathPlanContract.MaxPacketLength)
                throw new CorruptedFrameException("totalPacketLength out of range: " + h.TotalPacketLength);

            if (input.ReadableBytes() < (int)h.TotalPacketLength) return;      // 본문 대기

            byte[] raw = new byte[h.TotalPacketLength];                           // 상한 통과 후 NOW 복사
            input.ReadBytes(raw);                                                 // 정확히 totalPacketLength 만큼만 소비
            output.Add(PathPlanFrame.Parse(raw));                                 // ByteBuf 캡처 금지
            continue;
        }

        // ── 기존 0x02/0x04 경로: 길이 프리픽스 없음 → 고정 길이 테이블로만 경계를 잡는다 ──
        if (op is 0x02 or 0x04)
        {
            if (input.ReadableBytes() < 2) return;                              // 서브타입 대기
            byte sub = input.GetByte(input.ReaderIndex + 1);
            if (!LegacyPacketLength.TryGet(op, sub, out int fixedLen))
            {
                // ★ 길이를 <b>추측하지 않는다</b>. 모르는 조합은 fail-closed 로 종료한다.
                throw new CorruptedFrameException($"unknown legacy packet 0x{op:X2}/0x{sub:X2}");
            }
            if (input.ReadableBytes() < fixedLen) return;                       // 고정 길이 대기

            byte[] slice = new byte[fixedLen];
            input.ReadBytes(slice);                                               // 정확히 fixedLen 만큼만 소비
            output.Add(Unpooled.WrappedBuffer(slice));                            // "echo" 는 IByteBuffer 를 기대
            continue;
        }

        // ★ 알 수 없는 첫 바이트: <b>1바이트 건너뛰고 재탐색하지 않는다</b>(패킷 중간에서 동기화되어
        //   잘못된 경계를 만들 수 있다). 연결을 종료해 상태를 명확히 한다.
        throw new CorruptedFrameException("unknown leading byte: 0x" + op.ToString("X2"));
    }
}

internal static class LegacyPacketLength
{
    // EchoServerHandler.ChannelRead 가 실제로 읽는 최대 오프셋 + 1 (소스 기준, 미검증 가정).
    // ★ 이 테이블은 <b>기존 동작의 관찰값</b>이지 계약이 아니다. 클라이언트가 더 긴 패킷을
    //   보낼 가능성까지 배제하는 근거가 없으므로, 통합 시 <b>클라이언트 송신 코드와 대조 검증</b>이 선행되어야 한다.
    private static readonly (byte Op, byte Sub, int Len)[] Table =
    {
        (0x02, 0x00,  6), (0x02, 0x01, 25), (0x02, 0x02, 12),
        (0x04, 0x00, 19), (0x04, 0x01, 21), (0x04, 0x02, 11),
        (0x04, 0x03,  2), (0x04, 0x04, 28),
    };
    public static bool TryGet(byte op, byte sub, out int len)
    {
        foreach (var t in Table) if (t.Op == op && t.Sub == sub) { len = t.Len; return true; }
        len = 0; return false;
    }
}</pre>
<div class="stop"><b>선택지 B 의 전제 조건 (기본값이 아닌 이유).</b> 디스패처가 "고정 길이만큼 소비하고'''

new_section = '''<h3>8.2 공유 19100 포트 — (opcode, subtype) 기반 혼합 디스패처</h3>
<p><b>기존 19100 단일 포트</b>에서 0x02/0x04 레거시와 0x04/0x10·0x04/0x11 경로 프로토콜을 <b>(opcode, subtype) 두 바이트</b>로 분기해 처리한다. 별도 포트·별도 리스너를 만들지 않는다.</p>
<pre>// Program.cs RunServerAsync — 기존 ChildHandler를 교체 (제안, 미적용)
// 단일 bootstrap, 단일 파이프라인. MixedDispatcher를 "echo" 앞에 배치.
var boss   = new MultithreadEventLoopGroup(1);
var worker = new MultithreadEventLoopGroup();
var bootstrap = new ServerBootstrap();
bootstrap
    .Group(boss, worker)
    .Channel<TcpServerSocketChannel>()
    .Option(ChannelOption.SoBacklog, 100)
    .Option(ChannelOption.TcpNodelay, true)
    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
    {
        IChannelPipeline pipeline = channel.Pipeline;
        pipeline.AddLast("mixed.dispatch", new MixedProtocolDispatcher());  // (opcode, subtype) 분기
        pipeline.AddLast("echo", new EchoServerHandler());                   // 기존 핸들러 그대로
    }));
IChannel channel = await bootstrap.BindAsync(19100);
// PathPlanValidationContext 팩토리는 MixedDispatcher 내부에서 생성/주입.</pre>
<h3>8.3 MixedProtocolDispatcher — 상세 구현</h3>
<p>스트림 프레이밍 계층이 (opcode, subtype)을 peeking(인덱스 미전진)하여 각 메시지를 <b>정확히 한 번만 소비</b>하고 올바른 핸들러로 위임한다.</p>
<pre>// AI_Work/PathProtocol/MixedProtocolDispatcher.cs   (제안, 미생성)
// "echo" 핸들러 <b>앞</b>에 놓는다. 두 프로토콜을 한 누적 버퍼에서 순서대로 잘라내는 유일한 방법.
protected override void Decode(IChannelHandlerContext ctx, IByteBuffer input, List<object> output)
{
    for (;;)
    {
        // (0) ★ 최소 1바이트: TCP 는 메시지 경계를 보장하지 않는다. opcode 바이트조차 없을 수 있다.
        if (input.ReadableBytes() < 1) return;
        byte op = input.GetByte(input.ReaderIndex);        // ★ peek. readerIndex 를 움직이지 않는다.

        if (op == PathPlanContract.OpcodePathPlan)         // 0x04
        {
            // ── 0x04/0x10·0x04/0x11 경로: 자기경계 헤더 ──────────────────────────────
            if (input.ReadableBytes() < 2) return;                              // subtype 대기
            byte sub = input.GetByte(input.ReaderIndex + 1);
            if (sub != PathPlanContract.SubtypeRequest && sub != PathPlanContract.SubtypeResponse)
            {
                // 0x04인데 path-plan 서브타입이 아님 → 기존 0x04 고정길이 경로로 위임
            }
            else
            {
                if (input.ReadableBytes() < PathPlanContract.HeaderLength) return;   // 헤더 대기
                if (!PathPlanHeaderCodec.TryRead(input.GetReaderSlice(), out var h, out var err))
                {
                    if (err == PathPlanDecodeError.Truncated) return;
                    throw new CorruptedFrameException("bad path-plan header: " + err);
                }

                // 2) 상한 검증 — 메모리를 제한하는 유일한 지점. 할당보다 먼저.
                if (h.TotalPacketLength < PathPlanContract.MinPacketLength || h.TotalPacketLength > PathPlanContract.MaxPacketLength)
                    throw new CorruptedFrameException("totalPacketLength out of range: " + h.TotalPacketLength);

                // 서버 포트는 요청(0x10)만 수신. 응답(0x11)이 오면 방향 위반 → 연결 종료
                if (h.Subtype == PathPlanContract.SubtypeResponse)
                    throw new CorruptedFrameException("unexpected response on server port");

                if (input.ReadableBytes() < (int)h.TotalPacketLength) return;      // 본문 대기

                byte[] raw = new byte[h.TotalPacketLength];                           // 상한 통과 후 NOW 복사
                input.ReadBytes(raw);                                                 // 정확히 totalPacketLength 만큼만 소비
                output.Add(PathPlanFrame.Parse(raw));                                 // ByteBuf 캡처 금지
                continue;
            }
        }

        // ── 기존 0x02/0x04 경로: 길이 프리픽스 없음 → 고정 길이 테이블로만 경계를 잡는다 ──
        if (op == 0x02 || op == 0x04)
        {
            if (input.ReadableBytes() < 2) return;                              // 서브타입 대기
            byte sub = input.GetByte(input.ReaderIndex + 1);
            // 0x04/0x10, 0x04/0x11은 위에서 이미 처리됨. 여기선 나머지 서브타입만.
            if (op == 0x04 && (sub == PathPlanContract.SubtypeRequest || sub == PathPlanContract.SubtypeResponse))
            {
                // 이미 위 분기에서 처리됨 — 이론상 여기는 오지 않음
            }
            if (!LegacyPacketLength.TryGet(op, sub, out int fixedLen))
            {
                // ★ 길이를 <b>추측하지 않는다</b>. 모르는 조합은 fail-closed 로 종료한다.
                throw new CorruptedFrameException($"unknown legacy packet 0x{op:X2}/0x{sub:X2}");
            }
            if (input.ReadableBytes() < fixedLen) return;                       // 고정 길이 대기

            byte[] slice = new byte[fixedLen];
            input.ReadBytes(slice);                                               // 정확히 fixedLen 만큼만 소비
            output.Add(Unpooled.WrappedBuffer(slice));                            // "echo" 는 IByteBuffer 를 기대
            continue;
        }

        // ★ 알 수 없는 첫 바이트: <b>1바이트 건너뛰고 재탐색하지 않는다</b>(패킷 중간에서 동기화되어
        //   잘못된 경계를 만들 수 있다). 연결을 종료해 상태를 명확히 한다.
        throw new CorruptedFrameException("unknown leading byte: 0x" + op.ToString("X2"));
    }
}

internal static class LegacyPacketLength
{
    // EchoServerHandler.ChannelRead 가 실제로 읽는 최대 오프셋 + 1 (소스 기준, 미검증 가정).
    // ★ 이 테이블은 <b>기존 동작의 관찰값</b>이지 계약이 아니다. 클라이언트가 더 긴 패킷을
    //   보낼 가능성까지 배제하는 근거가 없으므로, 통합 시 <b>클라이언트 송신 코드와 대조 검증</b>이 선행되어야 한다.
    private static readonly (byte Op, byte Sub, int Len)[] Table =
    {
        (0x02, 0x00,  6), (0x02, 0x01, 25), (0x02, 0x02, 12),
        (0x04, 0x00, 19), (0x04, 0x01, 21), (0x04, 0x02, 11),
        (0x04, 0x03,  2), (0x04, 0x04, 28),
    };
    public static bool TryGet(byte op, byte sub, out int len)
    {
        foreach (var t in Table) if (t.Op == op && t.Sub == sub) { len = t.Len; return true; }
        len = 0; return false;
    }
}</pre>
<div class="ok"><b>이 디스패처가 지키는 불변식</b><br>
1) <b>(opcode, subtype) 두 바이트</b>로만 분기 — 0x04 하나만으로는 레거시와 신규를 가를 수 없다.<br>
2) 신규 경로(0x04/0x10, 0x04/0x11)는 <code>totalPacketLength</code>로 <b>자기경계</b>를 정의하고 정확히 그만큼만 소비.<br>
3) 레거시(0x02/*, 0x04/* 단 0x10/0x11 제외)는 고정 길이 테이블로 정확히 그만큼만 소비.<br>
4) 각 분기에서 <code>input.ReadBytes(...)</code>로 <b>정확히</b> 해당 프레임 바이트만 소비하므로, downstream <code>EchoServerHandler</code>가 보는 슬라이스는 항상 0번부터 시작하는 완전한 레거시 패킷이다.<br>
5) 알 수 없는 첫 바이트/조합은 즉시 연결 종료(fail-closed) — 재동기화 시도 없음.'''

content = content.replace(old_section, new_section)
print('Replaced:', old_section[:50] in content)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
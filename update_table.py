import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Update the integration table
old_table = '''<h3>8.6 통합 지점 (아직 미변경)</h3>
<table>
<thead><tr><th>기존 파일</th><th>구체 위치</th><th>필요한 작업</th><th>선행 조건</th></tr></thead>
<tbody>
<tr><td><code>NWO_RegionNode/Program.cs</code></td><td><code>RunServerAsync</code> — <code>ChildHandler</code> 는 19100 용 <code>AddLast("echo", …)</code> 한 줄만 있다</td><td>8.2 의 <b>두 번째 bootstrap(19101)</b> 추가. 기존 19100 <code>pipeline</code> 은 <b>한 줄도 바꾸지 않는다</b>. <code>PathPlanValidationContext</code> 팩토리(서버 navmesh, 버전, 시계, 선박별 sequence 저장소) 주입 지점 확보</td><td>프레이밍/핸들러 구현(8.4, 8.5)</td></tr>
<tr><td><code>NWO_RegionNode/EchoServerHandler.cs</code></td><td><code>ChannelRead</code> — <code>buffer.GetByte(0..)</code> 고정 인덱스, 0x02/0x04 만 처리</td><td><b>0x06 라우팅을 넣지 않는다.</b> 19101 파이프라인에 이 핸들러를 두지 않으므로 <code>ChannelRead</code> 는 손대지 않는다. 참고용 관찰: 이 핸들러는 <code>ctx.FireChannelRead</code> 도 호출하지 않고 수신 <code>IByteBuffer</code> 도 해제하지 않는다(<code>SimpleChannelInboundHandler</code> 로 바꾸면 자동 해제). <b>이번 범위 밖의 기존 동작이며 본 추가안에서 고치지 않는다</b></td><td>— (변경 불필요)</td></tr>
<tr><td><code>NWO_RegionNode/User.cs</code></td><td>현재 위치/수신 시각 보관 필드 부근</td><td>선박별 <code>LastObservedPlanSequence</code>, <code>RequestsInCurrentWindow</code>, <code>AuthoritativeShipPolygon</code>, 승인된 <code>serverPlanId</code> 보관 (판단 후)</td><td>세션 모델 확정</td></tr>
<tr><td><code>NWO_RegionNode/NWO_RegionNode.csproj</code></td><td>—</td><td><b>수정 불필요</b> (SDK 기본 glob이 AI_Work 하위 .cs 자동 포함, <code>DotNetty.Codecs</code> 는 이미 참조됨)</td><td>—</td></tr>
<tr><td><code>NWO_RegionNode/Terrain.cs</code></td><td><code>terrainCollision(x, y)</code></td><td><b>통합 대상 아님.</b> PNG 기반 지형 <i>높이</i> 평균 샘플링이며 navmesh 가 아니다. <code>IAuthoritativeNavMesh</code> 의 어떤 메서드도 대체하지 못한다 (6.4)</td><td>—</td></tr>
</tbody>
</table>'''

new_table = '''<h3>8.6 통합 지점 (아직 미변경)</h3>
<table>
<thead><tr><th>기존 파일</th><th>구체 위치</th><th>필요한 작업</th><th>선행 조건</th></tr></thead>
<tbody>
<tr><td><code>NWO_RegionNode/Program.cs</code></td><td><code>RunServerAsync</code> — <code>ChildHandler</code> 는 19100 용 <code>AddLast("echo", …)</code> 한 줄만 있다</td><td>8.2 의 <b>MixedProtocolDispatcher를 "echo" 앞에 추가</b> (단일 bootstrap, 단일 포트 19100). 기존 pipeline에 <code>pipeline.AddLast("mixed.dispatch", new MixedProtocolDispatcher())</code> 한 줄만 삽입. <code>PathPlanValidationContext</code> 팩토리(서버 navmesh, 버전, 시계, 선박별 sequence 저장소) 주입 지점 확보</td><td>프레이밍/핸들러 구현(8.4, 8.5)</td></tr>
<tr><td><code>NWO_RegionNode/EchoServerHandler.cs</code></td><td><code>ChannelRead</code> — <code>buffer.GetByte(0..)</code> 고정 인덱스, 0x02/0x04 만 처리</td><td><b>0x04/0x10·0x04/0x11 라우팅을 넣지 않는다.</b> MixedDispatcher가 0x04/0x10·0x04/0x11을 경로 검증기로, 0x02 및 0x04의 그 밖 서브타입을 이 핸들러로 정확히 소비해 위임하므로 새 경로 프레임이 이 핸들러의 오프셋 판독에 노출되지 않는다. <b>이번 범위 밖의 기존 동작이며 본 추가안에서 고치지 않는다</b></td><td>— (변경 불필요)</td></tr>
<tr><td><code>NWO_RegionNode/User.cs</code></td><td>현재 위치/수신 시각 보관 필드 부근</td><td>선박별 <code>LastObservedPlanSequence</code>, <code>RequestsInCurrentWindow</code>, <code>AuthoritativeShipPolygon</code>, 승인된 <code>serverPlanId</code> 보관 (판단 후)</td><td>세션 모델 확정</td></tr>
<tr><td><code>NWO_RegionNode/NWO_RegionNode.csproj</code></td><td>—</td><td><b>수정 불필요</b> (SDK 기본 glob이 AI_Work 하위 .cs 자동 포함, <code>DotNetty.Codecs</code> 는 이미 참조됨)</td><td>—</td></tr>
<tr><td><code>NWO_RegionNode/Terrain.cs</code></td><td><code>terrainCollision(x, y)</code></td><td><b>통합 대상 아님.</b> PNG 기반 지형 <i>높이</i> 평균 샘플링이며 navmesh 가 아니다. <code>IAuthoritativeNavMesh</code> 의 어떤 메서드도 대체하지 못한다 (6.4)</td><td>—</td></tr>
</tbody>
</table>'''

content = content.replace(old_table, new_table)
print('Replaced:', old_table[:50] in content)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
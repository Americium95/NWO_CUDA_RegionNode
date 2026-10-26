import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# The coordinate section HTML to insert (matching Plan.html)
coord_section = '''
<section>
<h2>7.1 좌표계 규약 (server global ↔ navmesh) — 확정/미확정 분리</h2>
<p>이 절은 서버 권위 좌표와 클라이언트 navmesh 좌표의 관계를 <b>확정된 것과 미확정인 것</b>으로 나눠 기록한다. 검증 로직은 다각형 인덱스만 다루므로 <b>어떤 좌표도 wire 에 실리지 않는다</b>(7장 클라이언트 매핑). 좌표는 서버 navmesh 내부에만 존재한다.</p>
<ul>
<li><b>확정 — 서버 글로벌 좌표.</b> <code>NWO_RegionNode/MoveMent.cs</code>의 <code>MoveMent.globalPosition</code>은 <code>nwo_Vector3</code>(<code>Int32 X, Int32 Y, Int32 Z</code>) 기반의 <b>대형 정수 XYZ</b> 좌표다. 실사용 범위/정밀도는 저장소에 정의되어 있지 않다.</li>
<li><b>확정 — 타일 나누기.</b> 같은 생성자에서 <code>tilePosition = (X/2560, Z/2560)</code>, <code>position = (X/2560, Y, Z/2560)</code>로 <b>X/Z 를 정수 2560 으로 나눈다</b>. 즉 서버가 사용하는 타일 좌표 단위는 <b>2560 좌표 단위(tile divisor 2560)</b>이며, 이 <b>2560</b>이 서버 측에서 확정된 유일한 수치다. 2560 의 물리적 의미(미터/센티미터/기타)는 저장소에 명시가 없다.</li>
<li><b>확정 — EchoServerHandler 잔여 스케일.</b> 같은 핸들러에서 타일 연산 후 좌표에 <b>×10</b>을 곱해 전달한다(<code>position * 10</code>). 이 10배 스케일이 서버 측 파이프라인에서 확정된 마지막 승수다.</li>
<li><b>미확정 — 대형 월드 전체의 스케일 크기.</b> 서버 저장소에서 확정된 것은 <b>타일 나누기 수치 2560 과 그 좌표 단위</b>뿐이다. 월드의 총 길이나 전역 축척(world scale)을 이 2560으로부터 환산해 <b>확정하지 않는다</b>. 알려진 것은 2560 좌표 단위까지다.</li>
<li><b>확정(구조) — 클라이언트 변환 표면.</b> 클라이언트 <code>ShipAI</code>는 <code>GeoNavMapTransform</code>/<code>NavMeshJsonRenderer</code> 를 통해 <b>스케일 적응된 대형 좌표</b>를 렌더링하고, 변환 파라미터 <code>centerPivot</code>, <code>worldScale</code>, <code>invertZ</code> 는 <b>구성 가능(configurable)</b>하게 노출된다. navmesh JSON(<code>format navmesh-merged-convex</code>, 정점 <code>[x, y]</code> with <code>y down</code>, <code>polygons</code>/<code>portals</code>/<code>centroids</code> 포함)이 선언하는 메타데이터는 <b>입력 계약</b>일 뿐 서버-클라이언트 스케일 대응을 증명하지 않는다.</li>
<li><b>미확정 — 원점·축/부호·스케일 대응.</b> 서버 글로벌 X/Y/Z 와 navmesh 로컬 좌표 사이의 <b>원점(origin)</b>, <b>축 매핑과 부호</b>(<code>invertZ</code>, y-down vs Z-up), <b>스케일 비율</b>은 <b>서버 저장소 코드에서 확정되어 있지 않다</b>(클라이언트 애셋은 서버 저장소 밖의 별도 체크아웃에 있다). 확정하려면 <b>실제 클라이언트 변환 소스</b>(<code>GeoNavMapTransform</code>/<code>NavMeshJsonRenderer</code>의 실제 <code>centerPivot</code>/<code>worldScale</code>/<code>invertZ</code> 값)와 <b>서버 좌표 소스</b>(<code>MoveMent.globalPosition</code> 및 실제 클라이언트 송신 좌표)를 <b>대조 캘리브레이션</b>해야 한다. 그 전까지는 <b>미해결/구성 가능</b> 항목으로 취급한다.</li>
<li><b>비권위 가정 금지.</b> navmesh JSON 이나 화면상의 격자/타일 개수로 <code>worldScale</code> 을 역산해 <b>확정하지 않는다</b>. 서버-클라이언트 좌표 대응은 반드시 <b>명시적 구성/캘리브레이션 파라미터</b>로 다뤄야 하며, 저장소 코드가 이를 정의하지 않는 한 특정 수치를 확정하지 않는다.</li>
<li><b>권위 소스.</b> <code>navmesh.json</code> 은 <b>서버가 다각형/포털 기하를 읽어들이는 소스</b>이며, 서버가 그 기하를 <b>소유</b>한다. 클라이언트는 <b>통과한 다각형 인덱스만</b> 순서대로 보내며 기하를 전송하지 않는다. 클라이언트가 보낸 어떤 수치도 좌표 근거로 사용하지 않는다.</li>
</ul>
<p class="warn">좌표 변환(서버 글로벌 XZ ↔ navmesh 로컬)의 비율·원점·축 부호는 <b>실제 클라이언트 변환(<code>GeoNavMapTransform</code>/<code>NavMeshJsonRenderer</code>)과 서버 좌표 소스(<code>MoveMent.cs</code>)를 기준으로 캘리브레이션</b>해 확정한다. 확정 전까지 <code>IAuthoritativeNavMesh</code>의 <code>Classify</code>/<code>LocatePolygon</code> 에 들어가는 nav 좌표의 절대 스케일은 <b>미해결</b>이며, 검증 로직은 <b>nav 좌표 한계 안에서 상대적으로만</b> 동작한다(비율·스텝 길이·통행 폭 임계값은 navmesh 빌드와 캘리브레이션 확정 후에 확정한다).</p>
</section>
'''

# Find the position to insert (after section 7, before section 8)
insert_pos = 64182

# Insert the coordinate section
new_content = content[:insert_pos] + coord_section + content[insert_pos:]

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(new_content)
print('Done')
import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix the 8.1 section - replace 0x06 references with new design
old_text = '''<div class="ok"><b>좋은 소식.</b> 새 0x06 헤더도 <b>첫 바이트가 opcode</b>(<code>0x06</code>)이고, 기존 opcode(<code>0x02</code>/<code>0x04</code>)와 값이 겹치지 않는다. 따라서 <code>buffer[0]</code> peeking(<b>인덱스 미전진</b>)만으로 두 프로토콜을 <b>안전하게 판별</b>할 수 있다. 이전 설계처럼 "첫 4바이트 길이 프리픽스"를 쓰면 <code>buffer[0]</code> 판별이 불가능했던 문제도, 12바이트 자기경계 헤더로 바뀌면서 <b>해결된다</b>.</div>'''

new_text = '''<div class="ok"><b>좋은 소식.</b> 새 경로 프로토콜은 <b>기존 0x04 opcode를 공유</b>하며 <b>subtype 0x10(요청)/0x11(응답)</b>으로 분기한다. 따라서 <code>buffer[0]</code>가 0x04일 때 <code>buffer[1]</code>(subtype)을 peeking(<b>인덱스 미전진</b>)하여 0x02/0x04 레거시와 0x04/0x10·0x04/0x11 경로 프로토콜을 <b>안전하게 판별</b>할 수 있다. opcode만으로는 두 경로를 가를 수 없고 <b>(opcode, subtype) 두 바이트</b>가 필요하다.</div>'''

content = content.replace(old_text, new_text)
print('Replaced:', old_text in content)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
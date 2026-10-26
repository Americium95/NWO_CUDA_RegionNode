import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace all remaining 0x06 references in the context of path plan protocol
# 1. "0x06 전용 리스너 포트" -> "공유 19100 포트 — (opcode, subtype) 기반 혼합 디스패처"
content = re.sub(r'0x06 전용 리스너 포트', '공유 19100 포트 — (opcode, subtype) 기반 혼합 디스패처', content)

# 2. "0x06 헤더" -> "0x04/0x10·0x04/0x11 헤더"
content = re.sub(r'0x06 헤더', '0x04/0x10·0x04/0x11 헤더', content)

# 3. "0x06 경로" -> "0x04/0x10·0x04/0x11 경로"
content = re.sub(r'0x06 경로', '0x04/0x10·0x04/0x11 경로', content)

# 4. "0x06 전용" -> "경로 프로토콜 전용"
content = re.sub(r'0x06 전용', '경로 프로토콜 전용', content)

# 5. "0x06 전용 파이프라인" -> "경로 프로토콜 파이프라인"
content = re.sub(r'0x06 전용 파이프라인', '경로 프로토콜 파이프라인', content)

# 6. "0x06 용" -> "경로 프로토콜용"
content = re.sub(r'0x06 용', '경로 프로토콜용', content)

# 7. "19101" in context of path plan -> "19100 (공유 포트)"
content = re.sub(r'19101', '19100 (공유 포트)', content)

# 8. Fix the risk comment about 0x06
content = re.sub(r'새 0x06 경로가 그 버퍼를 절대 통과하지 않으므로', '새 0x04/0x10·0x04/0x11 경로가 그 버퍼를 절대 통과하지 않으므로', content)

# 9. Fix "0x06 라우팅" -> "0x04/0x10·0x04/0x11 라우팅"
content = re.sub(r'0x06 라우팅', '0x04/0x10·0x04/0x11 라우팅', content)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
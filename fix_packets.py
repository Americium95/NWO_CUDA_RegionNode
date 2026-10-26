import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix request golden packet - replace the first line with correct bytes
# Old: 0x00    06 01 01 00 3C 00 00 00 01 00 00 00    opcode, mt, ver=1, total=0x3C=60, requestId=1
# New: 0x00    04 10 01 00 3C 00 00 00 01 00 00 00    opcode, subtype, ver=1, total=0x3C=60, requestId=1
content = re.sub(
    r'0x00\s+06 01 01 00 3C 00 00 00 01 00 00 00\s+opcode, mt, ver=1, total=0x3C=60, requestId=1',
    '0x00    04 10 01 00 3C 00 00 00 01 00 00 00    opcode, subtype, ver=1, total=0x3C=60, requestId=1',
    content
)

# Fix response golden packet
# Old: 0x00    06 02 01 00 27 00 00 00 01 00 00 00    opcode, mt, ver=1, total=0x27=39, requestId=1
# New: 0x00    04 11 01 00 27 00 00 00 01 00 00 00    opcode, subtype, ver=1, total=0x27=39, requestId=1
content = re.sub(
    r'0x00\s+06 02 01 00 27 00 00 00 01 00 00 00\s+opcode, mt, ver=1, total=0x27=39, requestId=1',
    '0x00    04 11 01 00 27 00 00 00 01 00 00 00    opcode, subtype, ver=1, total=0x27=39, requestId=1',
    content
)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
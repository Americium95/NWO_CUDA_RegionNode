import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix the last 0x06 reference in the risk comment
content = content.replace(
    '한 연결에서 뒤따르는 0x06 패킷은 사실상 도달 불가/오독된다',
    '한 연결에서 뒤따르는 0x04/0x10·0x04/0x11 패킷은 사실상 도달 불가/오독된다'
)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
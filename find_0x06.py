import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Find context around first 0x06
match = re.search(r'<div class="ok"><b>.*?0x06.*?</div>', content, re.DOTALL)
if match:
    with open('C:\\GitHub\\NWO_CUDA_RegionNode\\section_0x06.txt', 'w', encoding='utf-8') as out:
        out.write(match.group()[:2000])
    print('Written')
else:
    print('Not found')
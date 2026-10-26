import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the section 8.1 with the good news about 0x06
match = re.search(r'<div class="ok"><b>좋.*?0x06.*?</div>', content, re.DOTALL)
if match:
    with open('C:\\GitHub\\NWO_CUDA_RegionNode\\section_81.txt', 'w', encoding='utf-8') as out:
        out.write(match.group()[:3000])
    print('Written')
else:
    print('Not found')
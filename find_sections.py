import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the 8.2 and 8.3 sections
match = re.search(r'<h3>8\.2.*?<h3>8\.4', content, re.DOTALL)
if match:
    with open('C:\\GitHub\\NWO_CUDA_RegionNode\\section_8.txt', 'w', encoding='utf-8') as out:
        out.write(match.group()[:5000])
    print('Written to file')
else:
    print('Not found')
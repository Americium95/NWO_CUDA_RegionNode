import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the end of section 7 (before section 8)
match = re.search(r'</section>\s*<section>\s*<h2>8\.', content)
if match:
    with open('C:\\GitHub\\NWO_CUDA_RegionNode\\insert_pos.txt', 'w', encoding='utf-8') as out:
        out.write(str(match.start()))
    print('Found at:', match.start())
else:
    print('Not found')
import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Plan.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the coordinate section
match = re.search(r'<h2>7\.1.*?</section>', content, re.DOTALL)
if match:
    with open('C:\\GitHub\\NWO_CUDA_RegionNode\\plan_coord.txt', 'w', encoding='utf-8') as out:
        out.write(match.group()[:4000])
    print('Written')
else:
    print('Not found')
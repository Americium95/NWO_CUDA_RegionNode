import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Plan.html', 'r', encoding='utf-8') as f:
    plan = f.read()
    
with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    diff = f.read()

checks = {
    'Plan - 0x04/0x10 request': ('0x04/0x10' in plan) or ('0x10' in plan),
    'Plan - 0x04/0x11 response': ('0x04/0x11' in plan) or ('0x11' in plan),
    'Plan - 12-byte header': ('12 바이트' in plan) or ('HeaderLength' in plan),
    'Plan - totalPacketLength': 'totalPacketLength' in plan,
    'Plan - shared 19100': ('19100' in plan) and ('공유' in plan),
    'Plan - no 19101': '19101' not in plan,
    'Plan - no 0x06': '0x06' not in plan,
    'Plan - no MessageType': 'MessageType' not in plan,
    'Plan - coordinate section': '7.1 좌표계' in plan,
    'Plan - tile divisor 2560': '2560' in plan,
    'Plan - EchoServerHandler x10': ('x10' in plan.lower()) or ('10' in plan),
    'Plan - navmesh.json source': 'navmesh.json' in plan,
    'Plan - polygon indexes': ('polygonIndex' in plan) or ('polygon 인덱스' in plan),
    'Plan - reason 7': 'PATH_DISCONTINUITY' in plan,
    'Diff - 0x04/0x10 request': ('0x04/0x10' in diff) or ('SubtypeRequest' in diff and '0x10' in diff),
    'Diff - 0x04/0x11 response': ('0x04/0x11' in diff) or ('SubtypeResponse' in diff and '0x11' in diff),
    'Diff - 12-byte header': ('HeaderLength' in diff and '12' in diff) or ('1+1+2+4+4' in diff),
    'Diff - totalPacketLength': 'totalPacketLength' in diff,
    'Diff - shared 19100': ('19100' in diff) and (('공유' in diff) or ('shared' in diff.lower())),
    'Diff - no 19101': '19101' not in diff,
    'Diff - no 0x06': '0x06' not in diff,
    'Diff - no MessageType': 'MessageType' not in diff,
    'Diff - coordinate section': '7.1 좌표계' in diff,
    'Diff - tile divisor 2560': '2560' in diff,
    'Diff - EchoServerHandler x10': ('x10' in diff.lower()) or ('10' in diff),
    'Diff - navmesh.json source': 'navmesh.json' in diff,
    'Diff - polygon indexes': ('polygonIndex' in diff) or ('polygon 인덱스' in diff),
    'Diff - reason 7': 'PATH_DISCONTINUITY' in diff,
    'Diff - golden packet request 0x04 10': '04 10' in diff,
    'Diff - golden packet response 0x04 11': '04 11' in diff,
}

print('=== VERIFICATION RESULTS ===')
all_pass = True
for check, result in checks.items():
    status = 'PASS' if result else 'FAIL'
    if not result:
        all_pass = False
    print('  [{}] {}'.format(status, check))

overall = 'ALL PASS' if all_pass else 'SOME FAIL'
print('\nOverall: {}'.format(overall))
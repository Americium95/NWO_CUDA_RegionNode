import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Fix the PathPlanFrameDecoder - replace MessageType check with Subtype check
old_decoder = '''// (5) v1 은 요청만 받는다. 응답(0x02) 을 서버 포트에서 받는 것은 프로토콜 위반.
        if (h.MessageType != PathPlanContract.MessageTypeRequest)
            throw new CorruptedFrameException("expected request messageType");'''

new_decoder = '''// (5) v1 은 요청만 받는다. 응답(0x11) 을 서버 포트에서 받는 것은 프로토콜 위반.
        if (h.Subtype != PathPlanContract.SubtypeRequest)
            throw new CorruptedFrameException("expected request subtype");'''

content = content.replace(old_decoder, new_decoder)
print('Replaced decoder:', old_decoder in content)

# Also fix the comment about 0x06 pipeline
old_comment = '''// 0x06 전용 파이프라인에 다른 opcode 가 왔다 = 계약 위반(framing 불가).'''
new_comment = '''// 경로 프로토콜 파이프라인에 다른 opcode 가 왔다 = 계약 위반(framing 불가).'''

content = content.replace(old_comment, new_comment)
print('Replaced comment:', old_comment in content)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
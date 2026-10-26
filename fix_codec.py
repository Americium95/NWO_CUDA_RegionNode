import re

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace the TryRead method - first the variable name and check
content = re.sub(
    r'byte\s+msgType\s*=\s*src\[1\];',
    'byte   subtype = src[1];',
    content
)

# Replace the MessageTypeRequest check with SubtypeRequest
content = re.sub(
    r'msgType\s*!=\s*PathPlanContract\.MessageTypeRequest',
    'subtype != PathPlanContract.SubtypeRequest',
    content
)

# Replace the error code
content = re.sub(
    r'PathPlanDecodeError\.UnexpectedMessageType',
    'PathPlanDecodeError.UnexpectedSubtype',
    content
)

# Replace the header construction
content = re.sub(
    r'new PathPlanHeader\(opcode,\s*msgType,',
    'new PathPlanHeader(opcode, subtype,',
    content
)

# Replace the Write method - dst[1] = h.MessageType
content = re.sub(
    r'dst\[1\]\s*=\s*h\.MessageType;',
    'dst[1] = h.Subtype;',
    content
)

# Replace the enum value UnexpectedMessageType -> UnexpectedSubtype with comment
content = re.sub(
    r'UnexpectedMessageType,\s*//\s*[^,\n]*',
    'UnexpectedSubtype,          // 서버가 받는 방향은 SubtypeRequest(0x10) 뿐',
    content
)

with open(r'C:\GitHub\NWO_CUDA_RegionNode\NWO_RegionNode\AI_Work\ShipAI_ServerPathValidation_Implementation_Diff.html', 'w', encoding='utf-8') as f:
    f.write(content)
print('Done')
"""Build the version-specific x86 recovery stub. Python stdlib only.
No game files required. See README.md for the observed ABI and limits.
"""
from pathlib import Path
import struct,json
base=0x6e8000
external=[]
code=bytearray();labels={};refs=[]
def emit(h):code.extend(bytes.fromhex(h))
def u(n):code.extend(struct.pack('<I',n))
def mark(s):labels[s]=len(code)
def branch(op,label):emit(op);refs.append((len(code),label,'rel'));u(0)
def addr(label):refs.append((len(code),label,'abs'));u(0)
def call(va):emit('e8');external.append(len(code));u((va-(base+len(code)+4))&0xffffffff)
def checkbyte(off,value):emit('80 bd');u(off);emit(f'{value:02x}');branch('0f85','out')
def checkdword(off,value):emit('81 bd');u(off);u(value);branch('0f85','out')
def stringcheck(id,text,label):
 checkbyte(id*140+8,3);emit('8b 85');u(id*140);emit('83 78 04');emit(f'{len(text):02x}');branch('0f85','out');emit('8b 70 08 bf');addr(label);emit('b9');u(len(text)+1);emit('f3 66 a7');branch('0f85','out')
emit('9c 60 8b 74 24 28 8b 44 24 2c 81 b8');u(0x3448);u(0xd2550);branch('0f85','out')
emit('8b 96');u(0xf04)
signatures={861508: 16909056, 861512: 870145, 861516: 67196775}
for off in [0xd2544,0xd2548,0xd254c]:
 emit('81 ba');u(off);u(signatures[off]);branch('0f85','out')
emit('66 81 ba');u(0xd2550);emit('bf 02');branch('0f85','out')
emit('8b ae');u(0xf0c)
checkbyte(256*140+8,1);checkdword(256*140,1);checkdword(256*140+4,5)
# Preserve script pointer while string comparisons use ESI.
emit('53 8b de')
zone='Act2ZonesTriggerdec232ZIT_dec232_TRIG_end'
# Pop saved EBX before guards so all exits have identical stack depth.
emit('8b c6 5b 50')
# Save script pointer in a scratch register unused by string checks: EDX.
emit('5a')
stringcheck(254,zone,'zone');stringcheck(266,'NOD','nod');stringcheck(281,'SynchTimer_1','animname')
# EDX remains script; acquire native Level module context.
emit('8b 82');u(0xee0);emit('8b 00 8b 40 68 8b 58 0c 85 db');branch('0f84','out')
emit('83 bb');u(0x13c);emit('5f');branch('0f8e','out')
emit('68');addr('nodename');emit('8b 8b');u(0x1d8);call(0x4bd440);emit('85 c0');branch('0f84','out');emit('8b f8')
emit('8b 8b');u(0xf0);emit('8b 93');u(0xe8);emit('85 c9');branch('0f8e','start')
mark('scan');emit('39 7a 04');branch('0f84','out');emit('83 7a 08 5f');branch('0f84','out');emit('83 c2 20 49');branch('0f85','scan')
mark('start');emit('6a 01 6a 00 6a 00 6a 5f 57 8b cb');call(0x45cd50)
mark('out');emit('61 9d e9');external.append(len(code));u((0x42e330-(base+len(code)+4))&0xffffffff)
for label,s in [('zone',zone),('nod','NOD'),('animname','SynchTimer_1')]:mark(label);code.extend((s+'\0').encode('utf-16le'))
mark('nodename');code.extend(b'SynchTimer\0')
for off,label,kind in refs:struct.pack_into('<I',code,off,((base+labels[label]) if kind=='abs' else (labels[label]-off-4))&0xffffffff)

root=Path(__file__).resolve().parents[1]/'src/Core/Resources'
(root/'timerfix.bin').write_bytes(code)
(root/'timerfix.json').write_text(json.dumps({'base':base,'absoluteFixups':[o for o,l,k in refs if k=='abs'],'relativeExternalFixups':external,'entryOffset':0},indent=2))
print('Generated',len(code),'bytes in',root)

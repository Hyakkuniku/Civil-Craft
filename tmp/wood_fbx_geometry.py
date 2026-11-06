from pathlib import Path
import struct
import zlib

def read_fbx(path):
    data = Path(path).read_bytes()
    assert data.startswith(b'Kaydara FBX Binary'), 'Expected binary FBX'
    version = struct.unpack_from('<I', data, 23)[0]
    wide = version >= 7500
    header = '<QQQB' if wide else '<IIIB'
    size = struct.calcsize(header)
    def node(pos):
        end, count, prop_size, name_len = struct.unpack_from(header, data, pos)
        if not end: return None, pos + size
        pos += size
        name = data[pos:pos+name_len].decode('utf8'); pos += name_len
        props = []
        for _ in range(count):
            kind = chr(data[pos]); pos += 1
            if kind in 'YCFDIL':
                fmt = {'Y':'h','C':'?','F':'f','D':'d','I':'i','L':'q'}[kind]
                props.append(struct.unpack_from('<'+fmt,data,pos)[0]); pos += struct.calcsize(fmt)
            elif kind in 'SR':
                length = struct.unpack_from('<I',data,pos)[0]; pos += 4
                raw = data[pos:pos+length]; pos += length
                props.append(raw.decode('utf8',errors='replace') if kind=='S' else raw)
            elif kind in 'fdlibc':
                length, encoding, byte_count = struct.unpack_from('<III',data,pos); pos += 12
                raw=data[pos:pos+byte_count]; pos+=byte_count
                if encoding: raw=zlib.decompress(raw)
                fmt={'f':'f','d':'d','l':'q','i':'i','b':'b','c':'b'}[kind]
                props.append(struct.unpack('<'+str(length)+fmt,raw))
            else: raise ValueError(kind)
        children=[]
        while pos < end-size:
            child, after = node(pos)
            if child is None: break
            children.append(child); pos=after
        return (name,props,children),end
    nodes=[]; pos=27
    while pos<len(data)-size:
        value,pos=node(pos)
        if value is None:break
        nodes.append(value)
    return nodes

def walk(nodes):
    for value in nodes:
        yield value
        yield from walk(value[2])

if __name__=='__main__':
    nodes=read_fbx('Assets/Elements/mat_wood.fbx')
    for name,props,children in walk(nodes):
        if name in ('Geometry','Model'): print(name,props)
        if name=='P' and props[0] in ('UnitScaleFactor','UpAxis','FrontAxis','CoordAxis','Lcl Translation','Lcl Rotation','Lcl Scaling','GeometricTranslation','GeometricRotation','GeometricScaling'): print('PROPERTY',props)
        if name=='Vertices':
            xyz=list(zip(*[iter(props[0])]*3))
            print('VERTICES',len(xyz),'MIN',tuple(min(p[i] for p in xyz) for i in range(3)),'MAX',tuple(max(p[i] for p in xyz) for i in range(3)))

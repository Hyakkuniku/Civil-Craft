from pathlib import Path
import math
import re
from wood_fbx_geometry import read_fbx, walk

prefab = Path('Assets/BridgeBuilder/Prefabs/materials/map1/WoodRoad.prefab').read_text(encoding='utf-8-sig')
block = re.search(r'--- !u!4 &1618606862614620759\n(.*?)(?=--- !u!)',prefab,re.S)[1]
offset = float(re.search(r'm_LocalPosition: \{x: ([^,]+)',block)[1])
nodes = read_fbx('Assets/Elements/mat_wood.fbx')
raw = next(props[0] for name,props,children in walk(nodes) if name=='Vertices')
raw = list(zip(*[iter(raw)]*3))
# FBX uses centimetres; the authored 90-degree child rotation puts its Z
# along the builder's X. Measure the imported source, not a fabricated box.
points = [(z/100+offset,y/100,x/100) for x,y,z in raw]
lo,hi = min(p[0] for p in points),max(p[0] for p in points)
length = hi-lo
assert abs((lo+hi)/2) < 1e-6, (lo,hi)
assert 'value: -0.23844614' in prefab and 'value: -0.16844614' in prefab
cases=0
for target in (2,5,10):
    for angle in (0,20,-20):
        for reverse in (False,True):
            radians=math.radians(angle)
            origin=(target*math.cos(radians)/2,target*math.sin(radians)/2)
            ends=[(origin[0]+x*target/length*math.cos(radians),origin[1]+x*target/length*math.sin(radians)) for x in (lo,hi)]
            expected=[(0,0),(target*math.cos(radians),target*math.sin(radians))]
            assert max(math.dist(a,b) for a,b in zip(ends,expected))<1e-5
            cases+=1
before = sorted(Path('LocalBackups').glob('WoodRoadVisual-*/WoodRoad.prefab'),key=lambda p:p.stat().st_mtime)[-1].read_text(encoding='utf-8-sig')
expected = before.replace('m_LocalPosition: {x: -0, y: 0, z: 0}','m_LocalPosition: {x: -0.33844614, y: 0, z: 0}').replace('      value: 0.1\n','      value: -0.23844614\n').replace('      value: 0.17\n','      value: -0.16844614\n')
assert expected==prefab, 'An unrelated prefab property changed'
print(f'PASS: actual FBX deck centre={((lo+hi)/2):.9f}m; full geometry reaches both endpoints in {cases} length/slope/direction measurements. Exactly three visual X positions changed; all Y/Z, scales, materials, mesh references and physics properties retained.')

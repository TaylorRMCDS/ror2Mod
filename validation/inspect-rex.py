"""Read-only inspection of installed REX assets; requires UnityPy."""
from pathlib import Path
import json
import sys
import UnityPy

def detail(*args):
    if '--verbose' in sys.argv:
        print(*args)

bundles = Path(r'D:\SteamLibrary\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\aa\StandaloneWindows64')
env = UnityPy.load(str(next(bundles.glob('ror2-base-treebot_static_assets*.bundle'))))
objects = {o.path_id: o for o in env.objects}
for o in env.objects:
    if o.type.name != 'MonoBehaviour':
        continue
    d = o.read_typetree()
    name = d.get('m_Name', '')
    if 'serializedFieldsCollection' in d and any(s in name for s in ['Syringe', 'SonicBoom', 'FruitSeed']):
        detail(name)
        for f in d['serializedFieldsCollection']['serializedFields']:
            v = f['fieldValue']
            ref = objects.get(v.get('objectValue', {}).get('m_PathID'))
            detail(f['fieldName'], v['stringValue'], ref.read_typetree().get('m_Name') if ref else '')
    if name == 'bdFruiting':
        detail(json.dumps(d))
for o in env.objects:
    if o.type.name != 'GameObject':
        continue
    d = o.read()
    if any(s in d.m_Name for s in ['SyringeProjectile', 'Fruit', 'SonicBoom']):
        detail('GAMEOBJECT', d.m_Name)
        for c in d.m_Component:
            component = c.component.deref()
            if component.type.name == 'MonoBehaviour':
                detail(json.dumps(component.read_typetree()))

scripts = [o.read_typetree() for o in env.objects if o.type.name == 'MonoBehaviour']
states = {d['m_Name']: d for d in scripts if 'serializedFieldsCollection' in d}
gameobjects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}

def fields(name):
    return {f['fieldName']: f['fieldValue'] for f in states[name]['serializedFieldsCollection']['serializedFields']}

def components(name):
    return [c.component.deref().read_typetree() for c in gameobjects[name].read().m_Component]

syringe = fields('EntityStates.Treebot.Weapon.FireSyringe')
assert syringe['finalProjectilePrefab']['objectValue']['m_PathID'] == gameobjects['SyringeProjectileHealing'].path_id
assert syringe['projectilePrefab']['objectValue']['m_PathID'] == gameobjects['SyringeProjectile'].path_id
for name in ['SyringeProjectileHealing', 'SyringeProjectile']:
    assert next(c for c in components(name) if 'catalogIndex' in c)['procCoefficient'] == 0.5
print('PASS: Inject final and earlier shots use distinct projectiles, each originally proc 0.5.')
harvest = fields('EntityStates.Treebot.TreebotFireFruitSeed')
assert float(harvest['baseDuration']['stringValue']) == 1
assert float(harvest['damageCoefficient']['stringValue']) == 3.3
assert harvest['projectilePrefab']['objectValue']['m_PathID'] == gameobjects['TreebotFruitSeedProjectile'].path_id
assert next(c for c in components('TreebotFruitSeedProjectile') if 'damageType' in c)['damageType']['damageType'] == 536870912
print('PASS: Harvest fires the FruitOnHit projectile with 330% damage and 1-second recovery.')
sonic = fields('EntityStates.Treebot.Weapon.FireSonicBoom')
assert float(sonic['airKnockbackDistance']['stringValue']) == 8
assert float(sonic['groundKnockbackDistance']['stringValue']) == 0
assert float(sonic['slowDuration']['stringValue']) == 4
assert float(sonic['maxDistance']['stringValue']) == 30
print('PASS: Disperse baselines confirm airborne launch reference and 4-second Weaken.')
fruit = gameobjects['TreebotFruitPack']
pickup = next(d for d in scripts if 'fractionalHealing' in d and d['baseObject']['m_PathID'] == fruit.path_id)
assert pickup['fractionalHealing'] == 0.25 and pickup['flatHealing'] == 0
assert any(c.get('duration') == 20 for c in components('TreebotFruitPack'))
assert any('m_AssetId' in c for c in components('TreebotFruitPack'))
buff = next(d for d in scripts if d.get('m_Name') == 'bdFruiting')
assert not buff['canStack'] and not buff['isHidden'] and buff['isDebuff']
assert any('visualTransform' in c for c in components('FruitingEffect'))
print('PASS: Vanilla fruit is networked, heals 25%, lasts 20 seconds, and Fruiting has an existing world-space visual.')

# Borrow only the ring material, not the original distortion or movement.
effects = UnityPy.load(str(next(bundles.glob('ror2-base-treebot_assets*.bundle'))))
common = UnityPy.load(str(next(bundles.glob('ror2-base-common-vfx_assets*.bundle'))))
ring_material = next(o for o in common.objects if o.type.name == 'Material'
                     and o.read_typetree()['m_Name'] == 'matOmniRing1Generic')
material_data = ring_material.read_typetree()
assert 'VERTEXCOLOR' in material_data['m_ValidKeywords']
assert dict(material_data['m_SavedProperties']['m_Floats'])['_Cull'] == 0
shockwave = next(o for o in effects.objects if o.type.name == 'GameObject'
                 and o.read().m_Name == 'TreebotShockwaveEffect')
def renderer_materials(go):
    for component in go.read().m_Component:
        obj = component.component.deref()
        if obj.type.name == 'ParticleSystemRenderer':
            yield from obj.read_typetree()['m_Materials']
        elif obj.type.name == 'Transform':
            for child in obj.read().m_Children:
                yield from renderer_materials(child.deref().read().m_GameObject.deref())
assert any(m['m_PathID'] == ring_material.path_id for m in renderer_materials(shockwave))
print('PASS: Disperse has the reusable ring material with particle color support and two-sided rendering.')

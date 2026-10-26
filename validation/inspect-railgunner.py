"""Read-only installed Railgunner asset baselines. Requires UnityPy; not run by build.ps1."""
from pathlib import Path
import sys
import UnityPy

game = Path(sys.argv[1] if len(sys.argv) > 1 else r'D:\SteamLibrary\steamapps\common\Risk of Rain 2')
bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'
env = UnityPy.load(str(next(bundles.glob('ror2-dlc1-railgunner_static_assets*.bundle'))))
objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
          for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}


def fields(state):
    return {f['fieldName']: f['fieldValue']['stringValue']
            for f in states[state]['serializedFieldsCollection']['serializedFields']}


def components(name):
    return [(c.component.deref().type.name, c.component.deref().read_typetree())
            for c in objects[name].read().m_Component]


body = next(d for _, d in components('RailgunnerBody') if 'baseNameToken' in d)
assert body['baseNameToken'] == 'RAILGUNNER_BODY_NAME'
print('PASS: Railgunner body prefab and localization identity are verified.')
for name, sound in [('Heavy', 'Play_railgunner_m2_fire'), ('Light', 'Play_railgunner_m2_alt_fire')]:
    assert fields('EntityStates.Railgunner.Weapon.FireSnipe' + name)['fireSoundString'] == sound
print('PASS: Both secondary firing events are identified separately from specials/headshot audio.')
assert float(fields('EntityStates.Railgunner.Weapon.FireSnipeCryo')['bulletRadius']) == 2.0
assert fields('EntityStates.Railgunner.Weapon.FireSnipeCryo')['fireSoundString'] == 'Play_railgunner_R_alt_fire'
print('PASS: Cryocharge is a 2m-radius bullet; +20% produces a 2.4m shot radius, not a new explosion.')

mine = [d for _, d in components('RailgunnerMineAlt')]
blast = next(d for d in mine if 'blastRadius' in d)
assert blast['blastRadius'] == 10.0 and blast['blastProcCoefficient'] == 0.0
assert blast['blastDamageCoefficient'] == 1.0 and blast['childrenDamageCoefficient'] == 0.0
assert blast['childrenProjectilePrefab']['m_PathID'] == objects['RailgunnerMineAltDetonated'].path_id
assert float(fields('EntityStates.Railgunner.Weapon.FireMineBlinding')['damageCoefficient']) == 0.0
assert float(fields('EntityStates.Railgunner.Weapon.FireMineBlinding')['force']) == -500.0
print('PASS: Native Polar initial burst is 10m/zero damage/zero proc; its persistent child inherits zero damage.')

field = components('RailgunnerMineAltDetonated')
slow = next(d for _, d in field if 'slowDownCoefficient' in d)
ward = next(d for _, d in field if 'buffDef' in d and 'radius' in d)
sphere = next(d for kind, d in field if kind == 'SphereCollider')
assert abs(slow['slowDownCoefficient'] - 0.1) < 1e-6
assert ward['radius'] == 15.0 and sphere['m_Radius'] == 15.0
print('PASS: Lasting field already has a 15m ward/trigger and 10% projectile speed; native remaining speed stays 10%; no added projectile or enemy slow is installed.')

fuse_object = next(c.component.deref() for c in objects['RailgunnerMineAlt'].read().m_Component
                   if 'fuse' in c.component.deref().read_typetree())
fuse = fuse_object.read_typetree()
assert fuse['fuse'] == 1.0 and not fuse['m_Enabled'] and not fuse['subFuses']
stick = next(d for d in mine if 'stickEvent' in d)
assert any(c['m_Target']['m_PathID'] == fuse_object.path_id and
           c['m_MethodName'] == 'set_enabled' and c['m_Arguments']['m_BoolArgument']
           for c in stick['stickEvent']['m_PersistentCalls']['m_Calls'])
assert any(c['m_MethodName'] == 'Detonate' for c in fuse['onFuse']['m_PersistentCalls']['m_Calls'])
assert not blast['canRejectForce']
assert float(fields('EntityStates.Railgunner.Weapon.FireMineBlinding')['baseDuration']) == 0.23
print('PASS: Sticking enables the native 1s detonation fuse; quartering it gives 0.25s without changing flight speed/throw duration.')

camera_env = UnityPy.load(str(next(bundles.glob('ror2-base-core-camera_assets*.bundle'))))
rigs = [d for o in camera_env.objects if o.type.name == 'MonoBehaviour'
        for d in [o.read_typetree()] if 'sceneCam' in d]
listeners = [d for o in camera_env.objects if o.type.name == 'MonoBehaviour'
             for d in [o.read_typetree()] if d.get('isDefaultListener')]
assert rigs and listeners
for listener in listeners:
    # Follow the listener transform to its parent CameraRigController object.
    asset_file = next(o.assets_file for o in camera_env.objects if o.path_id == listener['m_GameObject']['m_PathID'])
    go = asset_file.objects[listener['m_GameObject']['m_PathID']]
    ancestors = set()
    while go:
        ancestors.add(go.path_id)
        transform = next(c.component.deref().read_typetree() for c in go.read().m_Component
                         if c.component.deref().type.name == 'Transform')
        parent_id = transform['m_Father']['m_PathID']
        if parent_id == 0: break
        parent = asset_file.objects[parent_id].read_typetree()
        go = asset_file.objects[parent['m_GameObject']['m_PathID']]
    assert any(r['m_GameObject']['m_PathID'] in ancestors for r in rigs)
print('PASS: Default gameplay audio listeners are children of camera rigs, supporting local-user ownership resolution.')

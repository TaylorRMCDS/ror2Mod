"""Read-only checks of the installed game's survivor assets; needs UnityPy."""
from pathlib import Path
import sys
import UnityPy

game = Path(sys.argv[1] if len(sys.argv) > 1 else r'D:\SteamLibrary\steamapps\common\Risk of Rain 2')
bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'
env = UnityPy.load(str(next(bundles.glob('ror2-base-commando_static_assets*.bundle'))))
objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
          for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}

def fields(state):
    return {f['fieldName']: f['fieldValue'] for f in states[state]['serializedFieldsCollection']['serializedFields']}

def components(name):
    return [c.component.deref().read_typetree() for c in objects[name].read().m_Component]

fmj = fields('EntityStates.Commando.CommandoWeapon.FireFMJ')
assert fmj['damageCoefficient']['stringValue'] == '3'
assert fmj['projectilePrefab']['objectValue']['m_PathID'] == objects['FMJRamping'].path_id
overlap = next(c for c in components('FMJRamping') if 'onServerHit' in c)
calls = overlap['onServerHit']['m_PersistentCalls']['m_Calls']
assert len(calls) == 1 and calls[0]['m_MethodName'] == 'AddToDamageCoefficient'
assert abs(calls[0]['m_Arguments']['m_FloatArgument'] - 0.4) < 1e-6
assert overlap['damageCoefficient'] == 1
print('PASS: Phase Round uses FMJRamping with a +0.4 per-hit additive ramp.')

shotgun = fields('EntityStates.Commando.CommandoWeapon.FireShotgunBlast')
assert shotgun['bulletCount']['stringValue'] == '4'
assert shotgun['damageCoefficient']['stringValue'] == '2'
assert shotgun['procCoefficient']['stringValue'] == '0.5'
assert float(shotgun['delayBetweenShotgunBlasts']['stringValue']) > 0
print('PASS: Phase Blast uses four pellets per blast, 200% damage and the expected proc path.')

grenade = components('CommandoGrenadeProjectile')
rigidbody = next(c for c in grenade if 'm_IsKinematic' in c)
assert not rigidbody['m_IsKinematic']
controller = next(c for c in grenade if 'authorityHandlesCollisionEvents' in c)
assert not controller['doNotPerformImpactOnCollision']
explosion = next(c for c in grenade if 'destroyOnEnemy' in c)
assert not explosion['destroyOnEnemy'] and not explosion['detonateOnEnemy']
print('PASS: Frag Grenade survives enemy collisions and uses a dynamic rigidbody.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-common_static_assets*.bundle'))))
buff = next(d for o in env.objects if o.type.name == 'MonoBehaviour'
            for d in [o.read_typetree()] if d.get('m_Name') == 'bdArmorBoost')
assert not buff['isHidden'] and buff['iconSprite']['m_PathID'] != 0
print('PASS: ArmorBoost has its standard visible buff icon.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-mage_static_assets*.bundle'))))
objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
scripts = [o for o in env.objects if o.type.name == 'MonoBehaviour']
states = {d['m_Name']: d for o in scripts for d in [o.read_typetree()]
          if 'serializedFieldsCollection' in d}
plasma = fields('EntityStates.Mage.Weapon.FireLightningBolt')
assert abs(float(plasma['damageCoefficient']['stringValue']) - 2.8) < 1e-6
assert plasma['projectilePrefab']['objectValue']['m_PathID'] == objects['MageLightningboltBasic'].path_id
assert float(fields('EntityStates.Mage.Weapon.Flamethrower')['maxDistance']['stringValue']) == 20
assert next(c for c in components('MageBody') if 'baseMoveSpeed' in c)['baseMoveSpeed'] == 7
assert next(c for c in components('MageIcewallPillarProjectile') if 'damageType' in c)['damageType']['damageSource'] == 4
print('PASS: Artificer projectile, damage, base speed, flame range and ice damage source match the patch.')

families = {d['m_Name']: (o, d) for o in scripts for d in [o.read_typetree()] if 'variants' in d}
def tokens(name):
    obj, data = families[name]
    return [obj.assets_file.objects[v['skillDef']['m_PathID']].read_typetree()['skillNameToken']
            for v in data['variants']]
assert tokens('MageBodyUtilityFamily')[0] == 'MAGE_UTILITY_ICE_NAME'
assert tokens('MageBodySpecialFamily')[1] == 'MAGE_SPECIAL_LIGHTNING_NAME'
assert len(tokens('MageBodyUtilityFamily')) == 2 and len(tokens('MageBodySpecialFamily')) == 2
print('PASS: The two Artificer families contain the expected swap targets and additional variants.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-bandit2_static_assets_all_*.bundle'))))
objects = {o.path_id: o for o in env.objects}
families = {d['m_Name']: (o, d) for o in env.objects if o.type.name == 'MonoBehaviour'
            for d in [o.read_typetree()] if 'variants' in d}
primary = families['Bandit2BodyPrimaryFamily'][1]['variants']
bandit_skills = [objects[v['skillDef']['m_PathID']].read_typetree() for v in primary]
rifle = next(s for s in bandit_skills if s['skillNameToken'] == 'BANDIT2_PRIMARY_ALT_NAME')
burst = next(s for s in bandit_skills if s['skillNameToken'] == 'BANDIT2_PRIMARY_NAME')
assert rifle['baseMaxStock'] == 4 and rifle['baseRechargeInterval'] == 0
assert burst['baseMaxStock'] == 4 and burst['baseRechargeInterval'] == 0
states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
          for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}
def state_fields(name):
    return {f['fieldName']: f['fieldValue']['stringValue'] for f in
            states[name]['serializedFieldsCollection']['serializedFields']}
rifle_state = state_fields('EntityStates.Bandit2.Weapon.Bandit2FireRifle')
assert rifle_state['bulletCount'] == '1'
assert rifle_state['minimumBaseDuration'] == '0.1'
assert rifle_state['spreadPitchScale'] == '0.5'
assert rifle_state['spreadYawScale'] == '1.3'
assert rifle_state['spreadBloomValue'] == '0.6'
crosshair = next(d for o in env.objects if o.type.name == 'MonoBehaviour'
                 for d in [o.read_typetree()] if 'skillStockSpriteDisplays' in d and
                 objects[d['m_GameObject']['m_PathID']].read_typetree()['m_Name'] == 'Bandit2Crosshair')
stock_displays = crosshair['skillStockSpriteDisplays']
stock_pip_ids = [9192060265309900957, 6105878987173367965,
                 5687415512878256285, -6460799673089027939]
stock_pips = [d for d in stock_displays if d['target']['m_PathID'] in stock_pip_ids]
assert len(stock_pips) == 4
assert sorted(d['minimumStockCountToBeValid'] for d in stock_pips) == [1, 2, 3, 4]
assert all(d['maximumStockCountToBeValid'] == 4 for d in stock_pips)
assert any(d['target']['m_PathID'] == -4364987360529510243 and
           d['minimumStockCountToBeValid'] == 1 and d['maximumStockCountToBeValid'] == 4
           for d in stock_displays)
assert any(d['target']['m_PathID'] == 4697279032780296349 and
           d['minimumStockCountToBeValid'] == 0 and d['maximumStockCountToBeValid'] == 0
           for d in stock_displays)
print('PASS: Blast has one projectile, four base charges and known firing values; the reticle confirms the four-stock display cap that 0.4.1 extends.')

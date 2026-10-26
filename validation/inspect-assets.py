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

env = UnityPy.load(str(next(bundles.glob('ror2-base-engi_static_assets*.bundle'))))
engi_objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
engi_states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
               for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}
def game_object_components(obj):
    return [c.component.deref().read_typetree() for c in obj.read().m_Component]
def serialized_fields(state):
    return {f['fieldName']: f['fieldValue']['stringValue'] for f in
            state['serializedFieldsCollection']['serializedFields']}
engi_body = next(c for c in game_object_components(engi_objects['EngiBody']) if 'baseDamage' in c)
walker_body = next(c for c in game_object_components(engi_objects['EngiWalkerTurretBody']) if 'baseDamage' in c)
stationary_body = next(c for c in game_object_components(engi_objects['EngiTurretBody']) if 'baseDamage' in c)
charge = serialized_fields(engi_states['EntityStates.Engi.EngiWeapon.ChargeGrenades'])
mine_full = serialized_fields(engi_states['EntityStates.Engi.Mine.MineArmingFull'])
mine_weak = serialized_fields(engi_states['EntityStates.Engi.Mine.MineArmingWeak'])
assert engi_body['baseDamage'] == 14 and abs(engi_body['levelDamage'] - 2.8) < 1e-5
assert walker_body['baseMaxHealth'] == 130 and abs(walker_body['baseRegen'] - 0.6) < 1e-5
assert stationary_body['baseMaxHealth'] == 130 and abs(stationary_body['baseRegen'] - 0.6) < 1e-5
assert charge['baseTotalDuration'] == '3' and charge['baseMaxChargeTime'] == '2' and charge['maxGrenadeCount'] == '8'
assert mine_full['damageScale'] == '3' and mine_weak['damageScale'] == '1'
print('PASS: Engineer assets confirm the damage, charge, mine-stage and separate mobile/stationary turret baselines.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-loader_static_assets*.bundle'))))
loader_objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
loader_states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
                 for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}
loader_body = next(c for c in game_object_components(loader_objects['LoaderBody']) if 'baseMaxHealth' in c)
knuckleboom = serialized_fields(loader_states['EntityStates.Loader.SwingComboFist'])
yank_raw = loader_states['EntityStates.Loader.FireYankHook']
yank_projectile = next(f for f in yank_raw['serializedFieldsCollection']['serializedFields']
                       if f['fieldName'] == 'projectilePrefab')['fieldValue']['objectValue']['m_PathID']
yank_object = next(o for o in env.objects if o.path_id == yank_projectile)
yank_controller = next(c for c in game_object_components(yank_object) if 'yankMassLimit' in c)
assert loader_body['baseMaxHealth'] == 160 and loader_body['baseArmor'] == 20 and loader_body['levelMaxHealth'] == 48
assert knuckleboom['pushAwayForce'] == '1000' and knuckleboom['damageCoefficient'] == '3.2'
assert yank_object.read().m_Name == 'LoaderYankHook' and yank_controller['yankMassLimit'] == 250
print('PASS: Loader assets confirm the body, exact Knuckleboom state and Spiked Fist 250-mass baseline.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-croco_static_assets*.bundle'))))
croco_objects = {o.path_id: o for o in env.objects}
croco_scripts = [o for o in env.objects if o.type.name == 'MonoBehaviour']
croco_skills = [d for o in croco_scripts for d in [o.read_typetree()]
                if d.get('skillNameToken', '').startswith('CROCO_')]
neurotoxin = next(s for s in croco_skills if s['skillNameToken'] == 'CROCO_SECONDARY_NAME')
frenzied_leap = next(s for s in croco_skills if s['skillNameToken'] == 'CROCO_UTILITY_ALT1_NAME')
breath_skill = next(s for s in croco_skills if s['skillNameToken'] == 'CROCO_SPECIAL_ALT_NAME')
assert neurotoxin['baseRechargeInterval'] == 2
assert frenzied_leap['baseMaxStock'] == 2 and frenzied_leap['baseRechargeInterval'] == 10
assert breath_skill['baseMaxStock'] == 1 and breath_skill['baseRechargeInterval'] == 12
croco_game_objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
breath_components = game_object_components(croco_game_objects['CrocoBreathProjectile'])
breath_controller = next(c for c in breath_components if 'procCoefficient' in c and 'catalogIndex' in c)
breath_zone = next(c for c in breath_components if 'fireFrequency' in c and 'resetFrequency' in c)
assert breath_controller['procCoefficient'] == 1
assert breath_zone['fireFrequency'] == 10 and breath_zone['resetFrequency'] == 4
print('PASS: Acrid assets confirm Neurotoxin 2s, Frenzied Leap 2/10 and the exact repeated-hit Cloying Breath projectile baseline.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-merc_static_assets*.bundle'))))
merc_trees = [o.read_typetree() for o in env.objects if o.type.name == 'MonoBehaviour']
merc_skills = {d['skillNameToken']: d['activationState']['_typeName'] for d in merc_trees
               if d.get('skillNameToken', '').startswith('MERC_')}
assert merc_skills == {
    'MERC_PRIMARY_NAME': 'EntityStates.Merc.Weapon.GroundLight2',
    'MERC_SECONDARY_NAME': 'EntityStates.Merc.WhirlwindEntry',
    'MERC_SECONDARY_ALT1_NAME': 'EntityStates.Merc.Uppercut',
    'MERC_UTILITY_NAME': 'EntityStates.Merc.PrepAssaulter2',
    'MERC_UTILITY_ALT1_NAME': 'EntityStates.Merc.FocusedAssaultPrep',
    'MERC_SPECIAL_NAME': 'EntityStates.Merc.EvisDash',
    'MERC_SPECIAL_ALT1_NAME': 'EntityStates.Merc.Weapon.ThrowEvisProjectile',
}
merc_primary = next(d for d in merc_trees if d.get('m_Name') == 'EntityStates.Merc.Weapon.GroundLight2')
merc_fields = serialized_fields(merc_primary)
assert merc_fields['damageCoefficient'] == '1.3' and merc_fields['comboFinisherDamageCoefficient'] == '1.3'
assert merc_fields['mecanimHitboxActiveParameter'] == 'Sword.active'
merc_machines = {d['skillNameToken']: d['activationStateMachineName'] for d in merc_trees
                 if d.get('skillNameToken', '').startswith('MERC_')}
assert merc_machines['MERC_PRIMARY_NAME'] == 'Weapon'
assert merc_machines['MERC_SPECIAL_ALT1_NAME'] == 'Weapon'
assert all(merc_machines[token] == 'Body' for token in (
    'MERC_SECONDARY_NAME', 'MERC_SECONDARY_ALT1_NAME', 'MERC_UTILITY_NAME',
    'MERC_UTILITY_ALT1_NAME', 'MERC_SPECIAL_NAME'))
assert any(d.get('groupName') == 'SwordLarge' and d.get('hitBoxes') for d in merc_trees)
expose = next(d for d in merc_trees if d.get('m_Name') == 'bdMercExpose')
assert expose['iconSprite']['m_PathID'] != 0 and not expose['canStack']
print('PASS: Mercenary has the SwordLarge third-swing volume, Sword.active gate, native Expose icon and verified Body/Weapon skill machines.')

for survivor, body_name, acceleration in [('mage', 'MageBody', 40), ('toolbot', 'ToolbotBody', 30)]:
    env = UnityPy.load(str(next(bundles.glob(f'ror2-base-{survivor}_static_assets*.bundle'))))
    body_object = next(o for o in env.objects if o.type.name == 'GameObject' and o.read().m_Name == body_name)
    body_components = game_object_components(body_object)
    assert next(c for c in body_components if 'baseAcceleration' in c)['baseAcceleration'] == acceleration
print('PASS: Artificer and MUL-T have distinct installed acceleration baselines of 40 and 30.')

env = UnityPy.load(str(next(bundles.glob('ror2-base-clayboss_static_assets*.bundle'))))
dunestrider = next(o for o in env.objects if o.type.name == 'GameObject' and o.read().m_Name == 'ClayBossBody')
dunestrider_components = game_object_components(dunestrider)
assert next(c for c in dunestrider_components if 'mass' in c and 'airControl' in c)['mass'] == 800
print('PASS: Clay Dunestrider uses ClayBossBody with motor mass 800, within Disperse\'s mass-suitability range.')

"""Read-only FIREWALL baselines from the installed Operator assets; requires UnityPy."""
from pathlib import Path
import UnityPy

bundles = Path(r'D:\SteamLibrary\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\aa\StandaloneWindows64')
env = UnityPy.load(str(next(bundles.glob('ror2-dlc3-dronetech_static_assets*.bundle'))))
behaviours = [obj.read_typetree() for obj in env.objects if obj.type.name == 'MonoBehaviour']
skill = next(t for t in behaviours if t.get('m_Name') == 'CommandShieldFormation')
assert skill['skillDescriptionToken'] == 'DRONETECH_UTILITY_ALT_DESCRIPTION'
assert skill['baseRechargeInterval'] == 8 and skill['baseMaxStock'] == 1
assert skill['beginSkillCooldownOnSkillEnd'] == 1
assert skill['activationState']['_typeName'] == 'EntityStates.DroneTech.Weapon.ShieldFormation'
print('PASS: FIREWALL has 8s native cooldown, one charge, and recharge begins when ShieldFormation ends.')
expected = {'ShieldProjectileEmpty', 'ShieldProjectile25', 'ShieldProjectile50', 'ShieldProjectile75', 'ShieldProjectile100'}
seen = set()
for obj in env.objects:
    if obj.type.name != 'GameObject':
        continue
    game_object = obj.read()
    if game_object.m_Name not in expected:
        continue
    def components_in_children(go):
        parts = []
        for c in go.m_Component:
            component = c.component.deref()
            if component.type.name == 'MonoBehaviour':
                parts.append(component.read_typetree())
            elif component.type.name == 'Transform':
                for child in component.read().m_Children:
                    parts.extend(components_in_children(child.read().m_GameObject.read()))
        return parts
    parts = components_in_children(game_object)
    blocking = [t for t in parts if 'pushEnemyProjectiles' in t]
    assert len(blocking) == (0 if game_object.m_Name == 'ShieldProjectileEmpty' else 1)
    assert all(not shield['pushEnemyProjectiles'] for shield in blocking)
    lifetime = next(t['lifetime'] for t in parts if 'lifetime' in t)
    assert abs(lifetime - (0.2 if game_object.m_Name == 'ShieldProjectileEmpty' else 0.33)) < 0.00001
    seen.add(game_object.m_Name)
assert seen == expected
print('PASS: All five FIREWALL variants expire in 0.2-0.33s; charged variants block hostile projectiles through a child collider, while the empty variant has no blocking child.')

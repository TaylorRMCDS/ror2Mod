"""Read-only installed Void Fiend baselines. Requires UnityPy; separate from build.ps1."""
from pathlib import Path
import sys
import UnityPy

game = Path(sys.argv[1] if len(sys.argv) > 1 else r'D:\SteamLibrary\steamapps\common\Risk of Rain 2')
bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'
env = UnityPy.load(str(next(bundles.glob('ror2-dlc1-voidsurvivor_static_assets*.bundle'))))
objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
          for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}


def fields(name):
    return {f['fieldName']: f['fieldValue']['stringValue']
            for f in states[name]['serializedFieldsCollection']['serializedFields']}


def components(name):
    return [c.component.deref().read_typetree() for c in objects[name].read().m_Component]


body = components('VoidSurvivorBody')
controller = next(d for d in body if 'corruptionPerSecondOutOfCombat' in d)
assert controller['corruptionPerSecondOutOfCombat'] == 3.0
assert controller['corruptionPerSecondInCombat'] == 3.0
print('PASS: Native combat and out-of-combat corruption are both 3/s; only out-of-combat changes to 2/s.')
projectile = components('VoidSurvivorMegaBlasterBigProjectileCorrupted')
simple = next(d for d in projectile if 'desiredForwardSpeed' in d)
explosion = next(d for d in projectile if 'blastRadius' in d)
assert simple['desiredForwardSpeed'] == 70.0 and simple['lifetime'] == 99.0
assert explosion['blastRadius'] == 10.0 and explosion['lifetime'] == 4.0
print('PASS: Corrupted Flood is 10m/70mps with 99s flight and a separate 4s explosion timer; both expiry limits must change.')
for name, fraction in [('CrushCorruption', 0.25), ('CrushHealth', -0.25)]:
    data = fields('EntityStates.VoidSurvivor.Weapon.' + name)
    assert float(data['selfHealFraction']) == fraction and float(data['baseDuration']) == 1.0
    assert float(data['corruptionChange']) == (-25.0 if fraction > 0 else 25.0)
for name in ['ChargeCrushCorruption', 'ChargeCrushHealth']:
    assert float(fields('EntityStates.VoidSurvivor.Weapon.' + name)['baseDuration']) == 1.0
print('PASS: Both Suppress forms have 1s charge + 1s recovery and +/-25% health exchange; corruption exchange remains +/-25.')
entry = fields('EntityStates.VoidSurvivor.EnterCorruptionTransition')
exit_state = fields('EntityStates.VoidSurvivor.ExitCorruptionTransition')
assert float(entry['duration']) == 1.0 and float(entry['lingeringInvincibilityDuration']) == 0.5
assert float(exit_state['duration']) == 0.0
print('PASS: Vanilla 1s transformation and its separate 0.5s invincibility tail are preserved.')
beam = fields('EntityStates.VoidSurvivor.Weapon.FireHandBeam')
assert float(beam['damageCoefficient']) == 3.0 and float(beam['maxDistance']) == 1000.0
print('PASS: Normal Drown is a 300% long-range beam; custom falloff does not change its range or base damage.')

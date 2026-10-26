"""Read-only Artificer/MUL-T/Mercenary movement and kill-attribution baselines."""
from pathlib import Path
import json
import sys
import xml.etree.ElementTree as ET
import UnityPy

props = ET.parse(Path(__file__).resolve().parents[1] / 'Directory.Build.props')
game = Path(sys.argv[1] if len(sys.argv) > 1 else props.findtext('./PropertyGroup/GameDirectory'))
bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'


def load(survivor):
    env = UnityPy.load(str(next(bundles.glob(f'ror2-base-{survivor}_static_assets*.bundle'))))
    states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
              for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}
    objects = {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}
    return env, states, objects


def fields(states, name):
    return {f['fieldName']: f['fieldValue']['stringValue'] for f in
            states[name]['serializedFieldsCollection']['serializedFields']}


env, states, objects = load('mage')
body = next(c.component.deref().read_typetree() for c in objects['MageBody'].read().m_Component
            if 'baseMoveSpeed' in c.component.deref().read_typetree())
assert body['baseMoveSpeed'] == 7
print('PASS: Vanilla Artificer base movement speed is 7; clamping the surge factor to 14 caps its native curve distance at 2x.')

env, states, objects = load('toolbot')
saw = [d for o in env.objects if o.type.name == 'MonoBehaviour'
       for d in [o.read_typetree()] if d.get('skillNameToken') == 'TOOLBOT_PRIMARY_ALT3_NAME']
assert saw and all('FireBuzzsaw' in d['activationState']['_typeName'] for d in saw)
print('PASS: Power-Saw has the verified skill token and FireBuzzsaw activation used to identify direct weapon-slot kills.')

env, states, objects = load('merc')
upper = fields(states, 'EntityStates.Merc.Uppercut')
assert float(upper['baseDuration']) == 0.4 and float(upper['baseDamageCoefficient']) == 5.5
curve = json.loads(upper['yVelocityCurve'])
assert curve['keys'][-1]['time'] == 1 and curve['keys'][-1]['value'] == 10
print('PASS: Uppercut native base duration is 0.4s and its final upward velocity is 10, which must not be multiplied by attack speed.')
for name in ['Assaulter2', 'FocusedAssaultDash']:
    state = fields(states, 'EntityStates.Merc.' + name)
    duration = 0.15 if name == 'Assaulter2' else 0.2
    assert float(state['baseDuration']) == duration and float(state['speedCoefficient']) == 20
    assert state['ignoreAttackSpeed'] == '1' and float(state['speedCoefficientOnExit']) == 0
    print(f'PASS: {name} natively ignores attack speed, has a {duration}s/20-speed dash and zero exit carry; scoped duration acceleration needs one distance correction.')
evis = fields(states, 'EntityStates.Merc.EvisDash')
assert float(evis['dashDuration']) == 0.2 and float(evis['dashPrepDuration']) == 0.1
assert float(evis['speedCoefficient']) == 15
print('PASS: Eviscerate entry has separate 0.1s preparation/0.2s movement phases; target search and damage phase remain distinct.')

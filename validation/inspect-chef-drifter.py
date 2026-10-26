"""Read-only baselines for the installed CHEF and Drifter assets; requires UnityPy."""
from pathlib import Path
import UnityPy

bundles = Path(r'D:\SteamLibrary\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\aa\StandaloneWindows64')


def load(pattern):
    env = UnityPy.load(str(next(bundles.glob(pattern))))
    return {tree['m_Name']: tree for obj in env.objects if obj.type.name == 'MonoBehaviour'
            for tree in [obj.read_typetree()] if tree.get('m_Name')}


def fields(tree):
    return {entry['fieldName']: entry['fieldValue']['stringValue']
            for entry in tree['serializedFieldsCollection']['serializedFields']}


chef = load('ror2-dlc2-chef_static_assets*.bundle')
ice = fields(chef['EntityStates.Chef.IceBox'])
assert float(ice['boostedProjectileDmgCoefficient']) == 15
assert chef['YesChef']['activationStateMachineName'] == 'Weapon'
print('PASS: Boosted Ice Box starts at 1500%; Yes CHEF executes on Weapon separately from Roll on Body.')
drifter = load('ror2-dlc3-drifter_static_assets*.bundle')
tornado = fields(drifter['EntityStates.Drifter.TornadoSlam'])
charge = fields(drifter['EntityStates.Drifter.ChargeTornadoSlam'])
assert float(tornado['damageCoefficient']) == 2.2
assert float(tornado['turnSpeed']) == 90 and float(charge['turnSpeed']) == 180
assert float(tornado['bounceMass']) == 200
print('PASS: Native Tornado Slam is 220% damage, 90 deg/s steering and mass-based ricochets; charge steering is 180 deg/s.')

# Enemy size classification is native again; no custom hull/mass categories remain.

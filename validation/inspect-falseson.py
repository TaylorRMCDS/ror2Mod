"""Read-only installed False Son laser baselines. Requires optional UnityPy."""
from pathlib import Path
import sys
import UnityPy

game = Path(sys.argv[1] if len(sys.argv) > 1 else r'D:\SteamLibrary\steamapps\common\Risk of Rain 2')
bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'
env = UnityPy.load(str(next(bundles.glob('ror2-dlc2-falseson_static_assets*.bundle'))))
states = {d['m_Name']: d for o in env.objects if o.type.name == 'MonoBehaviour'
          for d in [o.read_typetree()] if 'serializedFieldsCollection' in d}


def fields(name):
    return {f['fieldName']: f['fieldValue']['stringValue']
            for f in states[name]['serializedFieldsCollection']['serializedFields']}


laser = fields('EntityStates.FalseSon.LaserFatherCharged')
assert float(laser['baseFireFrequency']) == 8
assert float(laser['maximumDuration']) == 4
assert float(laser['procCoefficientPerTick']) == 0.45
assert float(laser['damageCoefficient']) == 1.75
assert float(laser['bulletCount']) == 1
burst = fields('EntityStates.FalseSon.LaserFatherBurstPrep')
assert float(burst['secondaryRefillPercentage']) == 0.5
print('PASS: Channeled laser fires 8 base ticks/s for 4s with 0.45 proc coefficient; 24 ticks is about 3s.')
print('PASS: Laser damage and bullet count retain their native values; Burst refills 50% before rounding.')

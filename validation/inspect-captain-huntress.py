"""Read-only Captain/Huntress asset regression checks. Requires optional UnityPy."""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import UnityPy

props = ET.parse(Path(__file__).resolve().parents[1] / 'Directory.Build.props')
game = Path(sys.argv[1] if len(sys.argv) > 1 else props.findtext('./PropertyGroup/GameDirectory'))
bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'


def load(pattern):
    env = UnityPy.load(str(next(bundles.glob(pattern))))
    return env, {o.read().m_Name: o for o in env.objects if o.type.name == 'GameObject'}


def components(obj):
    return [c.component.deref() for c in obj.read().m_Component]


def trees(obj):
    return [c.read_typetree() for c in components(obj)]


env, objects = load('ror2-base-captain_static_assets*.bundle')
ward = trees(objects['CaptainHealingWard'])
drop = trees(objects['CaptainSupplyDrop, Healing'])
assert not any('displayToken' in c for c in ward)
assert any(c.get('displayToken') == 'CAPTAIN_SUPPLY_HEAL_NAME' for c in drop)
healing = next(c for c in ward if 'healFraction' in c)
assert abs(healing['interval'] - 0.2) < 1e-6
assert abs(healing['healFraction'] - 0.02) < 1e-6
assert healing['healPoints'] == 0 and healing['radius'] == 10
assert any('defaultTeam' in c for c in ward)
print('PASS: CaptainHealingWard is separate from the named supply drop and heals 2% per 0.2s with its own team filter.')

env, objects = load('ror2-base-huntress_static_assets*.bundle')
rain = objects['HuntressArrowRain']
rain_trees = trees(rain)
zone = next(c for c in rain_trees if 'overlapProcCoefficient' in c)
controller = next(c for c in rain_trees if 'procCoefficient' in c)
assert zone['overlapProcCoefficient'] == 0.5 and controller['procCoefficient'] == 1
assert zone['fireFrequency'] == 20 and zone['resetFrequency'] == 3 and zone['lifetime'] == 6
assert zone['damageCoefficient'] > 0
print('PASS: Arrow Rain already has 0.5 native proc; native positive damage, tick cadence and lifetime are present.')

group = next(c for c in rain_trees if 'hitBoxes' in c)
assert len(group['hitBoxes']) == 1
hitbox = rain.assets_file.objects[group['hitBoxes'][0]['m_PathID']].read_typetree()
hitbox_go = rain.assets_file.objects[hitbox['m_GameObject']['m_PathID']]
transform = next(c for c in trees(hitbox_go) if 'm_LocalScale' in c)
scale = transform['m_LocalScale']
assert scale['x'] > 0 and scale['y'] > 0 and scale['z'] > 0
print('PASS: Arrow Rain has one native HitBox with positive transform dimensions to preserve horizontally.')

falling = next(o for o in env.objects if o.type.name == 'GameObject' and o.read().m_Name == 'ArrowsFalling')
particles = next(c.read_typetree() for c in components(falling) if c.type.name == 'ParticleSystem')
assert particles['ShapeModule']['enabled']
assert particles['InitialModule']['startSpeed']['scalar'] == 160
assert abs(particles['InitialModule']['startLifetime']['scalar'] - 0.1) < 1e-6
assert particles['scalingMode'] == 2  # Shape: retain native particle size/speed.
assert any(c.type.name == 'ParticleSystemRenderer' for c in components(falling))
print('PASS: The native falling-arrow emitter supports extending its shape without stretching particle size or speed.')

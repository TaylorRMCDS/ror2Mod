"""Check English tooltip coverage, native wording retention and rich-text balance."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
vanilla = json.loads((root / 'validation/vanilla-descriptions.json').read_text(encoding='utf-8'))
descriptions = {}
for source in (root / 'src').glob('*Balance.cs'):
    for token, literal in re.findall(r'"([A-Z0-9_]+)"\s*=>\s*("(?:[^"\\]|\\.)*")', source.read_text(encoding='utf-8-sig')):
        assert token not in descriptions, token
        descriptions[token] = json.loads(literal)
assert descriptions.keys() == vanilla.keys(), 'Missing or unexpected description token'
for token, text in descriptions.items():
    assert '<color=#C586FF><b>' in text, f'{token}: changes are not highlighted'
    assert not token.endswith('_NAME'), token
    stack = []
    for match in re.finditer(r'<(/?)(style|color|b|i)(?:=[^>]*)?>', text):
        close, tag = match.groups()
        if close:
            assert stack and stack.pop() == tag, f'{token}: mismatched {match[0]}'
        else:
            stack.append(tag)
    assert not stack, f'{token}: unclosed rich-text tags'
    # Every unmarked phrase must still be native wording in its original order.
    remainder = re.sub(r'<color=#C586FF><b>.*?</b></color>', '\0', text)
    original = re.sub(r'<[^>]+>', '', vanilla[token])
    cursor = 0
    for part in remainder.split('\0'):
        part = re.sub(r'<[^>]+>', '', part).strip()
        if not part:
            continue
        found = original.find(part, cursor)
        assert found >= 0, f'{token}: unmarked new wording {part!r}'
        cursor = found + len(part)

checks = {
    'CROCO_SECONDARY_DESCRIPTION': ['2.5 seconds'],
    'CROCO_UTILITY_ALT1_DESCRIPTION': ['8 seconds', '1 charge'],
    'MAGE_UTILITY_ALT_DESCRIPTION': ['1.1x straight down'],
    'ENGI_PRIMARY_DESCRIPTION': ['10% faster'],
    'ENGI_SPECIAL_ALT1_DESCRIPTION': ['2 health per second'],
    'HUNTRESS_SPECIAL_DESCRIPTION': ['236.25%', '100m'],
    'LOADER_SECONDARY_DESCRIPTION': ['50%', '1 second'],
    'RAILGUNNER_UTILITY_ALT_DESCRIPTION': ['0.1 seconds', '100% higher', '1500 force'],
    'VOIDSURVIVOR_PRIMARY_DESCRIPTION': ['50m', '100m'],
    'CHEF_UTILITY_DESCRIPTION': ['60-degree'],
    'DRIFTER_UTILITY_ALT_DESCRIPTION': ['3x normal', 'within 15m'],
}
for token, fragments in checks.items():
    plain = re.sub(r'<[^>]+>', '', descriptions[token])
    assert all(value in plain for value in fragments), token
for token in ['CROCO_PRIMARY_DESCRIPTION', 'LOADER_PRIMARY_DESCRIPTION', 'LOADER_HOOK_DESCRIPTION', 'COMMANDO_PRIMARY_DESCRIPTION']:
    assert token not in descriptions, f'{token}: unchanged or invalid token is overridden'
print(f'PASS: {len(descriptions)} tooltips preserve native wording outside bold purple changes; all rich-text tags balance.')

"""Read-only, filtered Unity bundle inspection. Requires optional UnityPy."""
import argparse
import fnmatch
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-directory', type=Path)
    parser.add_argument('--bundle', required=True, help='Bundle filename glob, e.g. ror2-base-mage_static_assets*.bundle')
    parser.add_argument('--name', default='*', help='Case-insensitive object name glob')
    parser.add_argument('--type', dest='object_type', choices=['GameObject', 'MonoBehaviour'],
                        help='Optional Unity object type filter')
    parser.add_argument('--list', action='store_true', help='List names and types instead of complete object trees')
    args = parser.parse_args()
    try:
        import UnityPy
    except ImportError:
        parser.error('UnityPy is not installed for this Python interpreter. See IMPLEMENTATION_PLAN.md for setup.')

    game = args.game_directory
    if game is None:
        props = ET.parse(Path(__file__).resolve().parents[1] / 'Directory.Build.props')
        game = Path(props.findtext('./PropertyGroup/GameDirectory'))
    bundles = game / 'Risk of Rain 2_Data/StreamingAssets/aa/StandaloneWindows64'
    paths = sorted(bundles.glob(args.bundle))
    if not paths:
        parser.error(f'No bundles matched {args.bundle!r} under {bundles}')

    found = 0
    unreadable = 0
    for path in paths:
        env = UnityPy.load(str(path))
        for obj in env.objects:
            kind = obj.type.name
            if kind not in ('GameObject', 'MonoBehaviour') or (args.object_type and kind != args.object_type):
                continue
            try:
                tree = None if kind == 'GameObject' else obj.read_typetree()
                name = obj.read().m_Name if tree is None else tree.get('m_Name', '')
                if not fnmatch.fnmatchcase(name.casefold(), args.name.casefold()):
                    continue
                found += 1
                print(f'{path.name} | {kind} | {obj.path_id} | {name}')
                if not args.list:
                    if tree is None:
                        tree = obj.read_typetree()
                    print(json.dumps(tree, indent=2, ensure_ascii=False, default=str))
            except Exception as error:
                unreadable += 1
                print(f'WARNING: {path.name} object {obj.path_id}: {error}', file=sys.stderr)
    if unreadable:
        print(f'WARNING: {unreadable} objects could not be fully inspected.', file=sys.stderr)
    if not found:
        parser.error(f'No readable objects matched {args.name!r}.')
    return 1 if unreadable else 0


if __name__ == '__main__':
    sys.exit(main())

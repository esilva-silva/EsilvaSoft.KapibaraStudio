"""Inventaria ZIPs Windows e bundles .NET sem executar seus binários.

Uso: python eng/audit-windows-licenses.py --packages <cache NuGet> --output <diretório> <ZIPs...>
O resultado é evidência técnica; licenças URL e componentes sem origem exigem revisão.
"""
import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zipfile
import zlib

MARKER = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def string(reader):
    length = shift = 0
    for _ in range(5):
        byte = reader.read(1)[0]
        length |= (byte & 127) << shift
        if not byte & 128:
            return reader.read(length).decode('utf-8')
        shift += 7
    raise ValueError('Comprimento de string inválido')


def bundle(data):
    marker = data.find(MARKER)
    if marker < 8:
        return []
    offset = struct.unpack_from('<q', data, marker - 8)[0]
    if offset == 0:
        return []
    reader = io.BytesIO(data)
    reader.seek(offset)
    major, minor, count = struct.unpack('<IIi', reader.read(12))
    if major not in (1, 2, 6) or minor != 0 or not 0 < count < 100000:
        raise ValueError('Formato de bundle não suportado')
    string(reader)
    if major >= 2:
        reader.read(40)
    entries = []
    for _ in range(count):
        position, size = struct.unpack('<qq', reader.read(16))
        compressed = struct.unpack('<q', reader.read(8))[0] if major >= 6 else 0
        kind = reader.read(1)[0]
        name = string(reader)
        if min(position, size, compressed) < 0 or position + (compressed or size) > len(data):
            raise ValueError('Entrada fora do bundle')
        payload = data[position:position + (compressed or size)]
        if compressed:
            payload = zlib.decompress(payload, -15)
        if len(payload) != size:
            raise ValueError('Tamanho inconsistente')
        entries.append((name, kind, payload))
    return entries


def write_csv(path, rows, fields):
    with path.open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def font_names(data):
    """Lê tabelas name de fontes sfnt incorporadas, sem carregar o assembly."""
    result = []
    for start in range(len(data) - 12):
        if data[start:start + 4] not in (b'\x00\x01\x00\x00', b'OTTO'):
            continue
        count = struct.unpack_from('>H', data, start + 4)[0]
        if not 5 <= count <= 50 or start + 12 + 16 * count > len(data):
            continue
        tables = {}
        for index in range(count):
            tag, _, offset, size = struct.unpack_from('>4sIII', data, start + 12 + index * 16)
            tables[tag] = offset, size
        if b'name' not in tables or b'head' not in tables:
            continue
        offset, size = tables[b'name']
        table = data[start + offset:start + offset + size]
        try:
            _, records, strings = struct.unpack_from('>HHH', table)
            names = []
            for index in range(records):
                platform, _, _, name_id, length, offset = struct.unpack_from('>HHHHHH', table, 6 + 12 * index)
                if name_id in (0, 1, 5, 13, 14):
                    raw = table[strings + offset:strings + offset + length]
                    names.append([name_id, raw.decode('utf-16-be' if platform in (0, 3) else 'latin1')])
            result.append(dict(offset=start, names=names))
        except (ValueError, struct.error, UnicodeError):
            continue
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source', type=Path, action='append', default=[],
                        help='Cache adicional de runtime, comparado por SHA-256')
    parser.add_argument('archives', type=Path, nargs='+')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rows, deps, archives, license_rows, runtime_packages = [], {}, [], [], set()
    for archive in args.archives:
        archives.append({'archive': archive.name, 'sha256': sha(archive.read_bytes())})
        with zipfile.ZipFile(archive) as zipped:
            for item in zipped.infolist():
                if item.is_dir():
                    continue
                data = zipped.read(item)
                rows.append(dict(archive=archive.name, container='ZIP', path=item.filename,
                                 size=len(data), sha256=sha(data), kind='external', origin=''))
                if 'license' in item.filename.lower() or 'notice' in item.filename.lower():
                    dest = args.output / 'archive-notices' / archive.stem / item.filename
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_bytes(data)
                if item.filename.endswith('.exe'):
                    for name, kind, payload in bundle(data):
                        rows.append(dict(archive=archive.name, container=item.filename, path=name,
                                         size=len(payload), sha256=sha(payload), kind=str(kind), origin=''))
                        if name.endswith('.runtimeconfig.json'):
                            options = json.loads(payload)['runtimeOptions']
                            for framework in options.get('includedFrameworks', []):
                                if framework['name'] == 'Microsoft.NETCore.App':
                                    rid = 'win-arm64' if 'win-arm64' in archive.name else 'win-x64'
                                    runtime_packages.add('Microsoft.NETCore.App.Runtime.' + rid + '/' + framework['version'])
                        if name.endswith('.runtimeconfig.json') or any(
                                word in Path(name).name.lower() for word in ('license', 'notice')):
                            dest = args.output / 'bundle-notices' / archive.stem / Path(item.filename).stem / name
                            dest.parent.mkdir(parents=True, exist_ok=True)
                            dest.write_bytes(payload)
                        if name.endswith('.deps.json'):
                            content = json.loads(payload)
                            deps[(archive.name, item.filename)] = content
                            dest = args.output / 'deps' / archive.stem / name
                            dest.parent.mkdir(parents=True, exist_ok=True)
                            dest.write_bytes(payload)
    packages = set(runtime_packages)
    for content in deps.values():
        packages.update(k for k, v in content['libraries'].items() if v['type'] == 'package')
    sources = {}
    wanted = {Path(r['path']).name.lower() for r in rows}
    for package in sorted(packages):
        name, version = package.rsplit('/', 1)
        root = args.packages / name.lower() / version.lower()
        nuspecs = list(root.glob('*.nuspec'))
        license_type = license_value = project_url = ''
        if nuspecs:
            dest = args.output / 'nuspecs' / name / version / nuspecs[0].name
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(nuspecs[0].read_bytes())
            xml = ET.parse(nuspecs[0]).getroot()
            for element in xml.iter():
                tag = element.tag.split('}')[-1]
                if tag == 'license':
                    license_type, license_value = element.get('type', ''), element.text or ''
                if tag == 'licenseUrl' and not license_value:
                    license_type, license_value = 'url', element.text or ''
                if tag == 'projectUrl':
                    project_url = element.text or ''
        notices = []
        for source in root.rglob('*'):
            if not source.is_file():
                continue
            if source.name.lower() in wanted:
                data = source.read_bytes()
                digest = sha(data)
                sources.setdefault(digest, []).append(package + ':' + source.relative_to(root).as_posix())
                if source.name == 'Avalonia.Fonts.Inter.dll' and 'net10.0' in source.parts:
                    (args.output / 'inter-fonts.json').write_text(
                        json.dumps(dict(assembly_sha256=digest, fonts=font_names(data)), indent=2), encoding='utf-8')
            if any(word in source.name.lower() for word in ('license', 'notice', 'copying')):
                notices.append(source.relative_to(root).as_posix())
                dest = args.output / 'package-notices' / name / version / source.relative_to(root)
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(source.read_bytes())
        license_rows.append(dict(package=package, license_type=license_type, license=license_value,
                                 project_url=project_url, notices='; '.join(notices), cache_present=root.exists()))
    for root in args.source:
        for source in root.rglob('*'):
            if source.is_file() and source.name.lower() in wanted:
                sources.setdefault(sha(source.read_bytes()), []).append(
                    'cache:' + root.name + ':' + source.relative_to(root).as_posix())
        for source in root.glob('*'):
            if source.is_file() and (source.name == 'package.json' or 'license' in source.name.lower()):
                dest = args.output / 'source-notices' / root.name / source.name
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(source.read_bytes())
    declarations = {r['package']: (r['license_type'] + ':' + r['license']) for r in license_rows}
    for row in rows:
        row['origin'] = '; '.join(sources.get(row['sha256'], []))
        if not row['origin'] and Path(row['path']).name.startswith('EsilvaSoft.KapibaraStudio'):
            row['origin'] = 'project:EsilvaSoft.KapibaraStudio (MIT)'
        scopes = set()
        for origin in row['origin'].split('; '):
            if origin.startswith('project:'):
                scopes.add('MIT (projeto)')
            elif origin.startswith('cache:'):
                scopes.add('GitHub Copilot CLI License 1.0.85; avisos de componentes adicionais')
            else:
                scopes.add(declarations.get(origin.split(':')[0], 'REVIEW_REQUIRED'))
        row['license_scope'] = '; '.join(sorted(scopes))
        if row['path'].endswith(('OneAuthInterop.dll', 'OneAuthInterop.LICENSE.txt', 'OneAuthInterop.dll.stamp.json')):
            row['license_scope'] += '; Microsoft Software License Terms (OneAuthInterop)'
        if row['path'].endswith(('msalruntime.dll', 'msalruntime_arm64.dll', 'msal-node-runtime.node',
                                 'CopilotComputerUse.exe', 'computer-use-mcp.exe', 'rg.exe', 'tgrep.exe')):
            row['license_scope'] += '; termos upstream individuais não fornecidos no ZIP/cache inspecionado'
        if 'Avalonia.Fonts.Inter.dll' in row['path']:
            row['license_scope'] += '; OFL-1.1 (fontes Inter incorporadas)'
    for package in license_rows:
        package['matched_records'] = sum(package['package'] + ':' in row['origin'] for row in rows)
    write_csv(args.output / 'files.csv', rows, ['archive', 'container', 'path', 'size', 'sha256', 'kind', 'origin', 'license_scope'])
    write_csv(args.output / 'packages.csv', license_rows,
              ['package', 'license_type', 'license', 'project_url', 'notices', 'cache_present', 'matched_records'])
    (args.output / 'archives.json').write_text(json.dumps(archives, indent=2), encoding='utf-8')
    print(json.dumps({'archives': archives, 'files': len(rows), 'packages': len(packages),
                      'unmatched': sum(not r['origin'] for r in rows)}, indent=2))


if __name__ == '__main__':
    main()

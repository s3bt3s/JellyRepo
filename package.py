"""Build a deterministic plugin ZIP and Jellyfin repository manifest (Python 3)."""
import argparse, hashlib, json, pathlib, zipfile, datetime, xml.etree.ElementTree as ET

p = argparse.ArgumentParser()
p.add_argument('--base-url', required=True, help='Public HTTPS directory hosting the ZIP')
p.add_argument('--output', default='dist')
a = p.parse_args()
if not a.base_url.startswith('https://'):
    p.error('--base-url must use HTTPS')
root = pathlib.Path(__file__).resolve().parent
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
out = pathlib.Path(a.output); out.mkdir(parents=True, exist_ok=True)
name = f'SimklWatched_{version}.zip'
dll = root / 'Jellyfin.Plugin.Simkl/bin/Release/net9.0/Jellyfin.Plugin.SimklWatched.dll'
with zipfile.ZipFile(out / name, 'w', zipfile.ZIP_DEFLATED) as z:
    z.write(dll, dll.name)
    z.write(root / 'LICENSE', 'LICENSE')
manifest = [{
    'guid': '29c23b82-7b4a-4f24-97d8-8567a618bcc3', 'name': 'Simkl Watched',
    'description': 'Envoie uniquement les marquages manuels Vu de Jellyfin vers SIMKL.',
    'overview': 'Marquage manuel Vu vers SIMKL', 'owner': 'Simkl Watched contributors',
    'category': 'General', 'versions': [{
        'version': version, 'changelog': 'Marquage manuel, file persistante et identification par série.',
        'targetAbi': '10.11.7.0', 'sourceUrl': a.base_url.rstrip('/') + '/' + name,
        'checksum': hashlib.md5((out / name).read_bytes()).hexdigest(),
        'timestamp': datetime.datetime.now(datetime.timezone.utc).isoformat()
    }]
}]
(out / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(out / name)
print(out / 'manifest.json')

"""Rebuild the MemoryDeck installer with Python 3 and NSIS 3 (makensis)."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent
os.chdir(ROOT)
manifest = json.loads((ROOT / 'runtime-provenance.json').read_text())
runtime = ROOT / 'payload' / 'runtime'
cache = ROOT / 'download-cache'
cache.mkdir(exist_ok=True)
for item in manifest['files']:
    archive = cache / Path(item['url']).name
    if not archive.exists() or hashlib.sha512(archive.read_bytes()).hexdigest() != item['hash']:
        print('Downloading official runtime:', archive.name, flush=True)
        with urllib.request.urlopen(item['url'], timeout=120) as response, archive.open('wb') as out:
            shutil.copyfileobj(response, out)
    if hashlib.sha512(archive.read_bytes()).hexdigest() != item['hash']:
        raise RuntimeError('Runtime SHA-512 verification failed: ' + archive.name)
    with zipfile.ZipFile(archive) as z:
        z.extractall(runtime)
compiler = os.environ.get('MAKENSIS') or shutil.which('makensis')
if not compiler and os.name == 'nt':
    candidate = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'NSIS' / 'makensis.exe'
    if candidate.is_file():
        compiler = str(candidate)
if not compiler:
    raise SystemExit('Install NSIS 3, then set MAKENSIS to makensis.exe or add it to PATH.')
(ROOT / 'dist').mkdir(exist_ok=True)
for script in ['launcher.nsi', 'setup.nsi']:
    subprocess.run([compiler, '-V3', script], check=True)
print('Built:', ROOT / 'dist' / 'MemoryDeck-1.2.2-Setup-win-x64.exe')

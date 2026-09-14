"""Skip unchanged .NET releases; choose the next patch from published NuGet versions."""
import io
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

FEED = 'https://api.nuget.org/v3-flatcontainer/cloudtoid.interprocess/'
# Include the library, packaged README, and its shared build/dependency settings.
INPUTS = ['src/dotnet/Interprocess', 'src/dotnet/README.md',
          'src/dotnet/Directory.Build.props', 'src/dotnet/Directory.Build.targets',
          'src/dotnet/Directory.Packages.props', 'src/dotnet/nuget.config',
          'src/dotnet/global.json', 'src/.editorconfig', 'global.json',
          'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']


def next_version(base, versions):
    assert re.fullmatch(r'\d+\.\d+\.0', base), 'Set VersionPrefix to major.minor.0'
    stable = sorted(tuple(map(int, v.split('.'))) for v in versions
                    if re.fullmatch(r'\d+\.\d+\.\d+', v))
    line = tuple(map(int, base.split('.')[:2]))
    if stable and line < stable[-1][:2]:
        raise ValueError('VersionPrefix is older than the latest NuGet release')
    patch = max((v[2] for v in stable if v[:2] == line), default=-1) + 1
    return '.'.join(map(str, (*line, patch))), '.'.join(map(str, stable[-1]))


def main():
    base = ET.parse('src/dotnet/Interprocess/Interprocess.csproj').findtext('.//VersionPrefix')
    with urllib.request.urlopen(FEED + 'index.json', timeout=60) as response:
        version, latest = next_version(base, json.load(response)['versions'])
    with urllib.request.urlopen(FEED + latest + '/cloudtoid.interprocess.' + latest + '.nupkg', timeout=60) as response:
        with zipfile.ZipFile(io.BytesIO(response.read())) as package:
            metadata = ET.fromstring(package.read('Cloudtoid.Interprocess.nuspec'))
    commit = metadata.find('.//{*}repository').get('commit', '')
    if not re.fullmatch(r'[0-9a-f]{40}', commit):
        raise ValueError('Published package is missing its source commit')
    subprocess.run(['git', 'merge-base', '--is-ancestor', commit, 'HEAD'], check=True)
    changed = subprocess.check_output(['git', 'diff', '--name-only', commit, 'HEAD', '--', *INPUTS], text=True)
    if changed:
        print(f'Publishing {version}; changed package inputs:\n{changed}')
        with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
            output.write(f'version={version}\n')
    else:
        print(f'No .NET package changes since {latest}; skipping NuGet.')


if __name__ == '__main__':
    main()

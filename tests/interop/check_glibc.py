"""Enforce the published glibc 2.34 floor on Linux release binaries."""
import re
import subprocess
import sys

for filename in sys.argv[1:]:
    symbols = subprocess.check_output(['objdump', '-T', filename], text=True)
    versions = [tuple(map(int, version.split('.'))) for version in re.findall(r'GLIBC_([0-9.]+)', symbols)]
    assert versions, f'{filename}: no glibc version requirements found'
    assert max(versions) <= (2, 34), f'{filename}: needs glibc {max(versions)}, above 2.34'
    print(f'{filename}: glibc requirements fit 2.34')

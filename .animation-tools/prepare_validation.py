from pathlib import Path
import shutil

workspace = Path.cwd()
target = workspace / '.demo1-animation-validation'
assert not target.exists()
target.mkdir()
def ignore_fonts(folder, names):
    return [name for name in names if name.lower().endswith(('.ttc', '.ttc.meta'))]
for name in ('Assets', 'Packages', 'ProjectSettings'):
    shutil.copytree(workspace/name, target/name, ignore=ignore_fonts)
(target/'Library').mkdir()
shutil.copytree(workspace/'Library/PackageCache', target/'Library/PackageCache')
print('Prepared isolated validation project; unused large system-font sources omitted.')

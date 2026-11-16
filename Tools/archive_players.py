"""Archive a prepared game folder without AppleDouble files or lost executable modes."""
from pathlib import Path
import argparse,os,stat,zipfile

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('stage',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
 root=a.stage.resolve();game=root/'Earth Unscripted'
 if not game.is_dir():p.error('Stage must contain the Earth Unscripted folder')
 with zipfile.ZipFile(a.output,'x',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
  for path in sorted(game.rglob('*')):
   if any(part.startswith('._') or part in {'.DS_Store','__MACOSX'} for part in path.relative_to(root).parts):continue
   name=str(path.relative_to(root))
   if path.is_symlink():
    info=zipfile.ZipInfo(name);info.create_system=3;info.external_attr=(stat.S_IFLNK|0o777)<<16;archive.writestr(info,os.readlink(path))
   elif path.is_file() or path.is_dir():archive.write(path,name)
 print(a.output.name,a.output.stat().st_size)
if __name__=='__main__':main()

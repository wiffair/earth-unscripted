"""Immutable packaged-worker entry; writable outputs belong in its cache only.

The profile builder is loaded from pinned source bytes to preserve per-region
state. Disable source-loader bytecode writes before importing it so a packaged
worker never adds __pycache__ files inside the signed game resources.
"""
import sys
import os

# Keep background GIS/linear algebra from expanding across all game cores.
for name in ("OPENBLAS_NUM_THREADS","OMP_NUM_THREADS","MKL_NUM_THREADS","VECLIB_MAXIMUM_THREADS","NUMEXPR_NUM_THREADS"):
    os.environ[name]="1"
if hasattr(os,"nice"):
    try:os.nice(5)
    except OSError:pass

sys.dont_write_bytecode = True
if __name__ == '__main__' and len(sys.argv)>1 and sys.argv[1]=='--public-map-fallback':
    del sys.argv[1]
    from public_overpass import main as public_map_main
    public_map_main()
    raise SystemExit(0)
if __name__ == '__main__' and len(sys.argv)>1 and sys.argv[1]=='--world-data':
    del sys.argv[1]
    from WorldData.__main__ import main as world_data_main
    world_data_main()
    raise SystemExit(0)


import area_pipeline
from map_transport import install as install_map_transport
install_map_transport(area_pipeline)

from WorldData.worker import install
install(area_pipeline)
from WorldData.public_terrain import install as install_public_surveys
install_public_surveys(area_pipeline)

from area_server import main


if __name__ == '__main__':
    main()

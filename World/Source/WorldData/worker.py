"""Add provenance to the existing terrain pipeline without changing geometry.

The selected provider binding is used by session_config. Finished-source metadata
is observed into a separate bounded cache; old prepared JSON/numeric identities
remain valid. No new external service is requested by this observer.
"""
import time
from .catalogue import Catalogue
from .cache import Store
from . import adapters

def install(pipeline_module):
    original=pipeline_module.Pipeline._prepare_locked
    if getattr(original,'world_data_observer',False):return
    def prepare(self,tile):
        result=original(self,tile);_,manifest,_=result
        if not hasattr(self,'world_data_store'):self.world_data_store=Store(self.cache/'WorldDataReceipts',16*1024*1024)
        for source in manifest.get('sourceReceipts',[]):
            name=source.get('file','');path=self.raw/name
            if path.parent!=self.raw or path.suffix.lower() not in ('.tif','.tiff') or not path.exists():continue
            quality='estimated' if name.startswith('00-coarse-') else 'measured'
            provider='terrain.global' if quality=='estimated' else 'terrain.ch' if self.frame.provider in ('switzerland','swiss-or-coarse') else __import__('WorldData.public_terrain',fromlist=['BINDINGS']).BINDINGS.get(self.frame.provider,('terrain.gb',))[0]
            role='surface' if '-dsm' in name and quality=='measured' else 'ground'
            key=self.world_data_store.key(provider,source.get('sha256',''),'native-raster@1','explicit-native-datum-v1',source.get('sha256',''))
            if self.world_data_store.get(key,time.time()) is not None:continue
            try:
                data=adapters.raster(path,role=role,provider=provider,received=time.time(),quality=quality,declared_units='m',vertical_datum=source.get('verticalDatum',self.frame.vertical_datum),
                    attribution=next((p['attribution'] for p in Catalogue().providers if p['id']==provider),provider))
                data.update(sourceHash=source.get('sha256',''),datasetVersion=source.get('sha256','unknown'))
                self.world_data_store.put(key,data)
                self.event('world-data-normalised',provider=provider,family=data['family'],quality=quality,role=role,sourceHash=source.get('sha256',''))
            except Exception as error:
                # Metadata failure cannot discard a previously validated world.
                self.event('world-data-metadata-unavailable',provider=provider,errorType=type(error).__name__)
        return result
    prepare.world_data_observer=True;pipeline_module.Pipeline._prepare_locked=prepare

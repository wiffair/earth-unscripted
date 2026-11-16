# Name and compatibility

The public name is **Earth Unscripted**. The application icon, startup/load
presentation, menus, HUD, product metadata and current documentation use it.

The following retain legacy spelling intentionally:

- `RoadRush` C# namespaces, shader/resource paths and native entry points.
- `games.blackwood.roadrush.beta.singleplayer` and its `.hd` bundle identifier.
- The established beta data directory ending in `Blackwood Games Beta/Road Rush Beta`.
- Existing save names, serialized values, `RoadRushVehiclePack`, `.rrcar` and
  world-pack formats, command-line proof switches, hashes and source identities.
- Original imported archives, historical releases and third-party legal notices.

Changing those with a global search/replace would strand careers, invalidate
signed/hashed packages or break integrations. `ProductIdentity` centralizes the
public name separately. Existing Mac, Windows and Linux beta paths are explicit;
new branded players continue to read their corresponding beta career and cache.

Renaming a release does not itself migrate or alter player saves. The Mac installer
keeps the old apps recoverable, updates the graphics chooser and preserves the
existing beta profile. Live-game saves remain a different product.

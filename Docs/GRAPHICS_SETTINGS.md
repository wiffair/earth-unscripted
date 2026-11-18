# Graphics settings

Choose Low, Medium, High or Ultra in Display & Audio. Presets change the scene rendering budget. The menus, HUD and text keep the selected display resolution.

| Preset | Scene resolution | Distant trees | Detailed trees nearby | Shadows | Driving frame limit |
| --- | --- | --- | --- | --- | --- |
| Low | 70% per dimension | Every fourth tree, within 900 m | Billboards | Off | 60 fps |
| Medium | 85% per dimension | Every second tree, within 1,600 m | Within 18 m | Within 90 m | 60 fps |
| High | 100% | Within 2,400 m | Within 45 m | Within 160 m | 90 fps |
| Ultra | 100% | Within 3,200 m | Within 65 m | Within 220 m | 120 fps |

Nearby building decoration and detailed woodland also use shorter drawing ranges at lower settings. HD uses fewer screen-space bounced-light samples on Low and Medium, and reduces ambient occlusion. Standard reduces anti-aliasing from four samples to two on those presets.

A 70% scene uses about 49% as many pixels as native resolution; 85% uses about 72%. These are pixel counts, not promised FPS gains. CPU work, reflections, scenery and the device also affect performance. HD uses software resolution scaling and sharpening; Standard uses its render scale.

In Standard, the detail buttons add 125% or 150% supersampling on High and Ultra. They use about 156% or 225% of native scene pixels and can substantially increase graphics load. Low and Medium keep their preset scale. Changing detail retains the selected quality preset. HD follows the preset and does not offer these unsupported supersampling controls.

Menus use a 30 fps limit. Driving uses the preset's frame limit; physics continues at 100 Hz. Loading retains its existing update budget so road preparation is not slowed by the menu limit.

Roads, terrain elevations and prepared map data are shared by all presets. Lower quality does not remove physical roads, change navigation, empty the cache or reduce traffic and police population. Tree collision selection follows the displayed tree density. Provider download speed and road preparation can still limit progress even when rendering is light.

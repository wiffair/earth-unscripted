# Interface design

Earth Unscripted uses a shared navy, warm-ivory and gold interface, an illustrated
journey landscape and a serif wordmark. The home page offers a clear primary
driving action, grouped navigation and current-location/vehicle summaries.
Secondary pages keep consistent controls, panel treatment and controller focus.

Display controls occupy one column; master audio, general feedback and independent
engine vibration occupy the other. Check marks are rasterized in their own local
coordinates, so scaled/letterboxed layouts cannot displace their strokes.

The driving HUD places location/time at the upper left, a larger heading-up map
at the lower left, and speed/condition with a separate limit disc at the lower
right. The compact retro radio remains below the speed instrument. Detailed
economy/weather and performance information are optional.

Pointer and controller controls invoke the same existing callbacks. Hold LB/L1
with D-pad left/right to tune, up/down for radio volume, and Y/Triangle for power.
Menu pause/resume retains the current radio position. Controller focus uses gold;
selection, disabled actions and pursuit restrictions remain explicit.

Startup uses native branded artwork, then the runtime loading screen with the
current preparation status and an activity indicator. Loading reuses valid cached
data; preparing graphics and collisions can still take time.

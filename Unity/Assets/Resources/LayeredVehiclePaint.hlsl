#ifndef EARTH_UNSCRIPTED_LAYERED_VEHICLE_PAINT
#define EARTH_UNSCRIPTED_LAYERED_VEHICLE_PAINT
// Shared art model for Standard and HD. The body remains opaque. Transmission
// describes a colour coat over the metallic substrate, not surface alpha.
// Refraction (IOR 1.5) bounds the optical path at grazing angles. Incoming light
// uses a normal-incidence approximation; native renderer lighting remains intact.
float3 EUCandyAttenuation(float3 transmission, float thickness, float normalView)
{
    float cosine = saturate(abs(normalView));
    float refractedCosine = sqrt(max(.01, 1 - (1 - cosine * cosine) / 2.25));
    float opticalPath = max(0, thickness) * (1 + rcp(refractedCosine));
    return pow(clamp(transmission, .001, 1), opticalPath);
}
float EUCandyFlakes(float3 localPosition, float scale, float strength)
{
    #if defined(SHADER_STAGE_RAY_TRACING)
    return 1;
    #else
    float3 cellPosition = localPosition * scale;
    // Fade individual flakes before they become subpixel sparkles/shimmer.
    float footprint = max(length(ddx(cellPosition)), length(ddy(cellPosition)));
    float visibility = 1 - smoothstep(.6, 2.2, footprint);
    float3 cell = floor(cellPosition);
    float seed = frac(sin(dot(cell, float3(127.1, 311.7, 74.7))) * 43758.5453);
    float sparkle = smoothstep(.88, .99, seed);
    return 1 + strength * visibility * (sparkle * .7 - .025);
    #endif
}
#endif

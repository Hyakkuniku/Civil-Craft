#ifndef CIVIL_CRAFT_BOAT_WAKE_INCLUDED
#define CIVIL_CRAFT_BOAT_WAKE_INCLUDED

// Supplied by BoatWaterWake. World positions are shared by all water cameras;
// no camera color/depth texture, additional foam, or mesh subdivision is needed.
float _CCBoatWakeCount;
float4 _CCBoatWakePosition[4]; // world X, waterline Y, world Z, movement strength
float4 _CCBoatWakeMotion[4];   // horizontal direction X/Z, hull half width/length

void CCBoatWaterRipple_float(float4 OriginalColor, float3 PositionWS,
    float Strength, float Width, float Length, float Spacing, float Speed,
    float Time, out float4 Color)
{
    Color = OriginalColor;
#ifndef SHADERGRAPH_PREVIEW
    if (Strength <= 0.0 || _CCBoatWakeCount < 0.5) return;

    float width = max(Width, 0.25);
    float wakeLength = max(Length, 1.0);
    float frequency = 6.2831853 / max(Spacing, 0.25);
    float phase = Time * max(Speed, 0.0);
    float disturbance = 0.0;
    [loop]
    for (int i = 0; i < 4; i++)
    {
        if (i >= (int)_CCBoatWakeCount) break;
        float4 boat = _CCBoatWakePosition[i];
        float4 motion = _CCBoatWakeMotion[i];
        if (boat.w <= 0.001) continue;

        float2 delta = PositionWS.xz - boat.xz;
        float halfWidth = max(motion.z, 0.25);
        float halfLength = max(motion.w, 0.5);
        // Reject remote surfaces and distant pixels before evaluating any waves.
        float reach = halfLength + wakeLength + halfWidth + width;
        if (dot(delta, delta) > reach * reach) continue;
        float draftTolerance = max(3.0, halfWidth + width);
        float verticalMask = 1.0 - smoothstep(draftTolerance,
            draftTolerance * 2.0, abs(PositionWS.y - boat.y));
        if (verticalMask <= 0.0) continue;

        float2 direction = motion.xy * rsqrt(max(dot(motion.xy, motion.xy), 0.0001));
        float along = dot(delta, direction);
        float lateral = abs(dot(delta, float2(-direction.y, direction.x)));

        // A bounded V-shaped trail widens gently behind the stern. Its bands
        // roll through the water instead of drawing a permanent white stripe.
        float behind = -along - halfLength * 0.6;
        float trailEnd = 1.0 - smoothstep(wakeLength * 0.65, wakeLength, behind);
        float trailStart = smoothstep(-width * 0.5, width, behind);
        float spread = halfWidth + max(behind, 0.0) * 0.25;
        float sideMask = 1.0 - smoothstep(width * 0.2, width, abs(lateral - spread));
        float trailMask = trailStart * trailEnd * sideMask;
        float trailWave = sin((behind * 0.65 + lateral * 1.4) * frequency - phase);

        // Short curved ripples around the bow join the trail without spreading
        // animation across the rest of the river.
        float2 bowDelta = float2(along - halfLength * 0.65, lateral);
        float bowDistance = length(bowDelta);
        float bowRadius = halfWidth + width;
        float bowMask = (1.0 - smoothstep(bowRadius * 0.4, bowRadius, bowDistance))
            * smoothstep(-halfLength, 0.0, along);
        float bowWave = sin(bowDistance * frequency - phase * 1.2);
        disturbance += (trailWave * trailMask + bowWave * bowMask * 0.65)
            * saturate(boat.w) * verticalMask;
    }

    // Signed brightness keeps the original water hue/alpha. This is NOT foam:
    // the unchanged contact-foam branch is blended after this function.
    Color.rgb = OriginalColor.rgb *
        (1.0 + clamp(disturbance, -1.0, 1.0) * saturate(Strength));
#endif
}

#endif

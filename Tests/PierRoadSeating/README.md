Run from the Unity project root:

```powershell
dotnet run --project Tests/PierRoadSeating/PierRoadSeating.csproj
```

The asset baseline was captured before the pier seating implementation. It protects both road and pier material assets, their imported visual sources and metadata, and the shared material physics/balancing code. This suite does not change strength, mass, cost, bridge nodes, colliders, player data, or scene assets.

Native `PierRoadSeatingValidation` complements these checks with the actual imported meshes and detached Unity geometry/rendering tests. Engine geometry is not inferred from screenshots or prefab YAML alone.

The gap regression also executes the production depth-overlap filter. A narrow cap must contact the central deck while ignoring deeper beams outside its footprint; a wide cap must still clear beams it actually overlaps. Dense independent edge samples check both non-penetration and zero contact gap across flat and positive/negative slopes. Native checks allow only 0.2 mm of floating-point tolerance. The original asset baseline is unchanged.

Road joints with different slopes additionally use a small, owned visual bearing above the cap's measured rectangular top face. The native checker includes 30°→5°, 5°→0°, reversed and negative grades, crests and valleys. It samples the rendered bearing triangles throughout the contact area and the cap centre; contact at only a bounding-box corner is insufficient. Gap/penetration tolerance is 0.3 mm. The bearing has no colliders and does not change the structural graph. Its mesh is rebuilt from serialized prism corners for prefab copies and host bridge snapshots, with separate ownership for ghost copies.

`PierCapSeatingProfileAuthoring.EnsureProfiles()` creates a new Resources catalog measured from the two approved source meshes. It never changes their prefabs, import settings, material values or contract availability. A changed source profile stops validation instead of silently replacing an existing catalog.

# Vehicle NPC obstacle geometry

Run `dotnet run --project Tests/VehicleNPCObstacleGeometry/VehicleNPCObstacleGeometry.csproj`.

The complete production helper is compiled against shape/transform boundary doubles using System.Numerics matrices. Checks cover rotated roots, independently rotated child colliders, scaled and mirrored roots, offset box/mesh shapes, sphere/capsule envelopes and invalid inputs. Native Unity checks should additionally compare the actual Ford collider and NavMeshObstacle envelopes. No gameplay, scene or player-save data is touched.

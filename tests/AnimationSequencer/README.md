# Animator regression tests

Build with `dotnet build tests/AnimationSequencer/AnimationSequencer.csproj`, then run Godot 4.7 Mono with `--headless --path tests/AnimationSequencer`. The process returns nonzero on failure.

These tests compile the production animation format and exercise serialization, native import/export, midpoint interpolation on an object and child light, discrete visibility, malformed values, and version 1 compatibility without launching the client or connecting to services.

## Editor check

1. Select a mesh, pawn, or other 3D instance in Explorer and open Animator. Create an animation.
2. Choose the root or a child object, then add Position, Rotation, Scale, or a property track.
3. Scrub or step to another frame, pose with the gizmo and press K (or enable Auto Key). Double-click a key to edit its components. Rotation transform tracks use degrees; colors use RGBA; booleans use 0/1.
4. Verify playback, snapping, zoom/scroll, key dragging, duplicate/delete, and undo/redo. Preview changes must not alter the source world object.
5. Save/reimport the `.bvanim`. Select the same source object when previewing relative property paths.
6. At runtime call `AnimationTrack.PlayOn(target)` to bind relative paths to a 3D instance; Play/Stop/Seek/AdjustSpeed control that player. This direct property player is local playback, not a replicated Animator command. Properties are Godot node properties; arbitrary script fields and resource-reference assignment are not supported.

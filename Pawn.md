# Pawn character rigs

`Pawn` is a `CharacterModel` that can replace a player's visual character. It keeps the existing `Player` body, health, inventory, replication, and respawn flow. Add renderable children to the Pawn, including an imported `Mesh` for a realistic character or car body. Its children move with the player. Set visual mesh `CanCollide` to `false` so Pawn owns the character collision.

Create and possess a Pawn from a **server** script:

```luau
local pawn = Pawn.New()
pawn.Name = "RaceCar"
pawn.Parent = Environment

-- Attach a Mesh and other visual children to pawn before possession.
player:Possess(pawn)
```

`player.ControlledPawn` and `pawn.Controller` expose the current relationship. `pawn:Unpossess()` or `player:Unpossess()` restores the default Brickversian rig. Possession destroys the previous rig, so clone a reusable template before possessing it. `PawnChanged`, `Possessed`, and `Unpossessed` are signals for scripts to update effects or UI.

To spawn every player with one rig, place a Pawn under `PlayerDefaults` and assign it to `PlayerDefaults.PawnTemplate`. The engine clones that template during player creation. The template can contain meshes, `Attachment` instances, and a `SurfaceAppearance`. Assign `SurfaceAppearance` to an imported `Mesh` or the Pawn root to apply color, normal, roughness, and metalness maps. Imported mesh materials remain available when the appearance is disabled.

For camera and held tools, set `CameraAttachment` and `ToolAttachment` to child `Dynamic` or `Attachment` instances. The Pawn also looks for a child named after each `CharacterAttachment` enum value, such as `Head` or `HandRight`; if absent, it uses its own root. This permits car camera mounts without human bones.

`CollisionShapeType`, `CollisionSize`, and `CollisionOffset` configure the collision shape linked to the Player body. Box and capsule shapes are supported; `None` disables Pawn collision. A default box matches the Brickversian character footprint. The shape updates when the Pawn is resized or its collision properties change.

In `Scripted` mode, local scripts can use `InputVector` (X steering, Y forward throttle), `JumpPressed`, `SprintPressed`, and `ControlTick` to drive the Pawn with `pawn:Move(velocity)` or `pawn:Jump()`. Input is zero when the game is not focused or the player cannot move. `Default` mode retains standard walking controls. `ControlMode` can be changed during possession. The Pawn also exposes `Velocity`, `Health`, `IsOnGround`, `IsLocallyControlled`, and the base `CharacterModel` animation API.

Pawn changes the character presentation, collision footprint, and control policy. Vehicle suspension and wheel physics require a scripted controller or separate physics work.

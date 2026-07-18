# Voxel Destruction

Real-time voxel destruction on a GPU raymarcher in Unity.

Voxel models are stored as a `Texture3D` with a colour palette and raymarched on the GPU, so there is no
mesh: the volume is rendered by marching a ray through the texture per pixel. This repo is what I built
on top of that idea: making the volume **destructible**, **collidable**, and **editable at runtime**.

**This is a source showcase, not a buildable project.** It's here to be read, and it won't compile as-is.
It contains only the C# I wrote. The renderer itself is not mine and is not included, I did however convert it to from URP to BIRP.

## What I wrote

| File | Lines | Role |
|---|---|---|
| `src/VoxelChunk.cs` | 1942 | The core. A destructible voxel volume: loads the model, owns the `Texture3D` and palette, applies edits, and rebuilds collision as the volume changes. |
| `src/VoxelInteractionManager.cs` | 266 | Runtime editing. Raycast into the volume, remove voxels within a spherical radius or spray-paint them, and spawn rigidbody debris for what comes off. |
| `src/FlyCamera.cs` | 186 | Free-look camera for moving through the scene. |
| `src/ColliderPool.cs` | 74 | A recycled pool of `BoxCollider`s. Destruction churns colliders constantly, so pooling is what stops it from lagging. |
| `src/Texture3DAtlas.cs` | 57 | Flattens a `Texture3D` into a single-row `Texture2D` atlas of Z-slices, for platforms where 3D textures are awkward. |
| `src/Spawner.cs` | 54 | Grid spawning of test models. |

## Models load at runtime

The original renderer builds the volume **outside the program**. Its demo materials point at Z-slice atlases that were
flattened ahead of time and committed as PNGs, with the dimensions baked into the filename:
`castle.(21x1).png`, `chr_knight.(21x1).png`, `3D Voxel Office Pack.(31x16).png`. Changing a model means
regenerating that texture in another tool and reassigning it.

`VoxelChunk` reads a MagicaVoxel **`.vox` file at runtime instead**, and builds the `Texture3D` and the
256x1 palette texture in-engine. Parsing the `.vox` container is the third-party **VoxReader** package
(`com.sandrofigo.voxreader`). The volume is an **R8** texture of raw palette indices with the palette kept separate, uploaded as a byte array rather than `Color` structs, so it costs one byte per voxel instead of four.

This is also what makes the rest possible: a volume built in memory at load time is a volume you can edit
at runtime. A baked atlas isn't.

## Collision

The original renderer lists per-voxel sphere colliders under things it deliberately *didn't* build, and it's
right that the naive version doesn't work: a modest model is tens of thousands of voxels, and a collider
each is not survivable.

Instead, surface voxels are merged into a much smaller set of **multi-resolution box colliders**, pooled and
rebuilt incrementally as the volume is edited. A large model becomes collidable without a collider per
voxel, and destruction stays real-time because colliders are recycled rather than reallocated.

---

Pulled from Project-Voxel (private). Original systems by LostPizzaMan.

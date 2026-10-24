# Player visual prefabs

The nine assets in `Prefabs/PlayerVisuals` replace the old Player children in `Scenes/WorldScene/World.unity`.
PlayerAuthoring accepts either prefab assets or legacy child parts; duplicate CharacterPart values are rejected.
Prefab references bake into PlayerVisualPrefab requests, not shared live render entities.
PlayerVisualSpawnSystem consumes requests in Initialization, instantiates an independent copy per owner,
sets Parent, registers linked entities for owner destruction, and populates PlayerVisualPart.
Animation, light-depth, normal, equipment and silhouette systems continue to use PlayerVisualPart unchanged.
Original meshes, material references and local transforms are preserved. Edit visual authoring in the prefabs.

Manual validation: enter Play Mode, move/jump/rotate, equip both hands and check normal/shadow rendering;
then remove the player and confirm its visual entities are destroyed without leaving orphan renderers.

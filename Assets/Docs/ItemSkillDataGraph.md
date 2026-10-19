# Successful-data item skills

## Authoring

Item skills now use one visible connection per successful result, not a Flow wire plus a duplicate Position wire. Ports are named after values: Item, Position, World Item, Block, Shape, Distance. The skill starts with its invoking Item context. Cursor/Distance/Item-definition nodes are constants: they configure a step but never invoke an action independently.

A step only passes evaluated data to its connected successor after its check succeeds. Runtime context retains the invoking player/hand/item alongside Position and World Item data. A World Item result carries the exact ECS Entity and its resolved surface position. Range checks forward those values unchanged. Failed checks stop the path before mutation. Branches select one matching row; unrelated row outputs do not execute.

New skills, actions, conditions and existing-action/condition additions from the graph menus use this approach. Existing serialized chain GUIDs and numeric kinds are retained for all eight shipped skills: Pick, Throw, Torch Ignite, Extinguish, Rubedo, Albedo, Nigredo and Flint Ignite. Legacy skill arrays remain readable but are hidden from the ordinary Inspector; no active shipped skill uses them. Internal Next/PositionSource fields remain serialization details, not a second user-facing control graph. A single graph connection updates the continuation and its data binding together. Disconnecting it stops execution of that branch.

The runner retains the existing validate-before-mutation guarantee and 64-step/cycle guard. Conditions must precede actions. This is deliberately not an arbitrary asynchronous event graph or physics simulation. Pick/Set World Block use chain-specific execution without their legacy action-distance constraints: the connected Within Distance check is authoritative. Legacy direct action calls retain their original safeguards. Changes to action/configuration types rebuild editor ports automatically.

## World items at position

Use the World Items At Position condition (serialized kind 8). Its required first input is a successful Position. Add as many Item rows as needed with + Item:

- Left Item input: connect a Data — Item node selecting the ItemDefinition to match by ID.
- Right World Item output: connect the corresponding action or further check.

It scans grounded, live WorldItem instances on that exact XZ cell and surface height, including half-block heights. It checks rows in order; for the first matching row it picks the closest instance to the cell centre, breaking equal-distance ties by entity index. One item/branch is selected per skill use. Airborne items, wrong floors and nonmatching IDs do not produce output. Matching and replacement are independent: the condition has no replacement action reference.

Replace World Item takes the resolved World Item entity and a single Replacement Item configuration. It has no cursor lookup, list of mappings, range check, hand condition or Ignite knowledge. Execution revalidates entity existence, grounded/live state and the resolved position, verifies registration of the replacement, then changes only the instance definition/resource cap and decay timer. Position, physics and remaining resource are retained. The Inspector hides its unused inherited legacy Distance field.

## Flint Ignite

Item → Other Hand Item → Top Surface Position → Within Distance → World Items At Position → Replace World Item.

Other Hand Item requires Flint. Top Surface Position captures the cursor's actual Top surface, including half blocks. Within Distance shares PickUpDistance (2 units), before the search. The Torch row receives Data Item: Torch and sends its World Item to Replace World Item configured as IgnitedTorch. Only the common hand gate controls skill visibility; finding no cursor target does not hide the skill. No equipment target can reach the replacement action.

## Verification

Both runtime and editor C# compile checks pass. SkillDataGraphChecks.ps1 validates all eight shipped skill graphs, asset references, branching, cycles, source ancestry, Ignite ordering, Pick distance and generic replacement configuration. It executes actual extracted OutputName logic with object adapters. SkillDataExecutionChecks.ps1 runs the actual extracted runner with ECS/action/cursor adapters and real Unity.Mathematics: failures prevent mutation, only matching branches execute, the same entity/position reaches replacement, range precedes lookup, positionless actions work, Pick forwards its entity and cycles fail. FlintChecks.ps1 runs actual surface/resource helpers. These are not real Unity gameplay/action implementation tests and do not replace Play-mode validation of dragging/deleting ports, Undo/Redo, all skill interactions, light activation or save/reload. Reopen the Item Graph after compilation to rebuild its ports.

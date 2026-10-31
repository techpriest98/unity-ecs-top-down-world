# Successful-data item skills

## Authoring

Item skills now use one visible connection per successful result, not a Flow wire plus a duplicate Position wire. Ports are named after values: Item, Position, World Item, Block, Shape, Distance. The skill starts with its invoking Item context. Cursor/Distance/Item-definition nodes are constants: they configure a step but never invoke an action independently.

A step only passes evaluated data to its connected successor after its check succeeds. Runtime context retains the invoking player/hand/item alongside Position and World Item data. A World Item result carries the exact ECS Entity and its resolved surface position. Range checks forward those values unchanged. Failed checks stop the path before mutation. Branches select one matching row; unrelated row outputs do not execute.

New skills, actions, conditions and existing-action/condition additions from the graph menus use this approach. Existing serialized execution-chain GUIDs and numeric kinds are retained for all eleven shipped skills, including Cut Bark, Gather and Craft Stick. Legacy skill arrays remain readable but are hidden from the ordinary Inspector; no active shipped skill uses them. Internal Next/PositionSource fields remain serialization details. A single graph connection updates the continuation and its data binding together. Disconnecting it stops execution of that branch.

## Skill visibility

Each skill has two output ports. Visibility starts VisibilityNode and evaluates a read-only condition graph to decide whether the HUD shows the skill. An unconnected Visibility output means always visible. When activated, ExecuteSkill evaluates Visibility once more using current state, then passes its resolved Position, target Entity and tree instance ID to Execute. It never reuses a previous HUD result.

Data — Visibility Result starts the execution graph for targeted skills and forwards those resolved values without checking the cursor or searching again. Their Execute paths contain this data node, Within Distance and the action. Visibility conditions do not need duplicate nodes in Execute. Actions retain their own final validation of the exact resolved target immediately before mutation. A raw Execute call without a visibility evaluation cannot fabricate Visibility Result data.

Every condition connected to Visibility participates; there is no ControlsVisibility flag. Actions are rejected by the editor and by the visibility evaluator. The Visible terminal ends a successful branch, including matching-item and selection-kind branches. Missing conditions, unconnected matched branches and cycles hide the skill. A Within Distance node affects visibility only when explicitly placed in this graph.

Shipped visibility graphs check the grounded item for Pick Up; the Top target and fire source for Ignite Torch; the other hand, Top surface and matching world item for Flint Ignite; and the pine resin condition and resolved tree for Cut Bark/Gather. Their execution range checks stay in Execute. Nigredo's selection-kind branch routes execution, and does not need a visibility branch. The item-library baker follows both graph roots.

The runner retains the existing validate-before-mutation guarantee and 64-step/cycle guard. Conditions must precede actions. This is deliberately not an arbitrary asynchronous event graph or physics simulation. Pick/Set World Block use chain-specific execution without their legacy action-distance constraints: the connected Within Distance check is authoritative. Legacy direct action calls retain their original safeguards. Changes to action/configuration types rebuild editor ports automatically.

## World items at position

Use the World Items At Position condition (serialized kind 8). Its required first input is a successful Position. Add as many Item rows as needed with + Item:

- Left Item input: connect a Data — Item node selecting the ItemDefinition to match by ID.
- Right World Item output: connect the corresponding action or further check.

It scans grounded, live WorldItem instances on that exact XZ cell and surface height, including half-block heights. It checks rows in order; for the first matching row it picks the closest instance to the cell centre, breaking equal-distance ties by entity index. One item/branch is selected per skill use. Airborne items, wrong floors and nonmatching IDs do not produce output. Matching and replacement are independent: the condition has no replacement action reference.

Replace World Item takes the resolved World Item entity and a single Replacement Item configuration. It has no cursor lookup, list of mappings, range check, hand condition or Ignite knowledge. Execution revalidates entity existence, grounded/live state and the resolved position, verifies registration of the replacement, then changes only the instance definition/resource cap and decay timer. Position, physics and remaining resource are retained. The Inspector hides its unused inherited legacy Distance field.

## Flint Ignite

Execute → Visibility Result → Within Distance → Replace World Item.

Visibility follows Other Hand Item → Top Surface Position → World Items At Position → Visible. Top Surface Position captures the cursor's actual Top surface, including half blocks. The Torch row receives Data Item: Torch. Its exact resolved World Item is passed to Execute, whose Within Distance shares PickUpDistance (2 units), then Replace World Item changes it to IgnitedTorch. No duplicate hand/target check nodes or second item search are needed in Execute. No equipment target can reach the replacement action.

## Verification

Both runtime and editor C# compile checks pass. SkillDataGraphChecks.ps1 validates all ten shipped skills and both graph roots, asset references, branching, cycles, source ancestry, absence of actions in Visibility, Ignite ordering, Pick distance and generic replacement configuration. It executes actual extracted OutputName logic with object adapters. SkillDataExecutionChecks.ps1 runs the actual extracted runner with ECS/action/cursor adapters and real Unity.Mathematics: failures prevent mutation, matching branches preserve exact targets, execution and visibility ranges are independent, explicit conditions control visibility, actions cannot run in Visibility, unconnected Visibility stays visible and cycles fail. These checks do not replace Play-mode validation of port dragging/deletion, Undo/Redo and HUD interactions. Reopen the Item Graph after compilation to rebuild its ports.

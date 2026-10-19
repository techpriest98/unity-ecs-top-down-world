using Game.Items.Skills;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Game.Items
{
    public static class WorldItemMatchUtility
    {
        public static Entity Find(EntityManager manager, float3 position, WorldItemMatch[] rows, out int matchedRow)
        {
            matchedRow = -1;
            if (!math.all(math.isfinite(position)) || rows == null) return Entity.Null;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldItem>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            int3 cell = (int3)math.floor(position);
            // Row order is priority. Within a row, choose the closest item to
            // the cell centre; entity index breaks ties deterministically.
            for (int row = 0; row < rows.Length; row++)
            {
                if (rows[row]?.Item == null || rows[row].Item.Id <= 0) continue;
                Entity found = Entity.Null; float best = float.MaxValue;
                foreach (var entity in entities)
                {
                    var item = manager.GetComponentData<WorldItem>(entity);
                    if (item.Instance.DefinitionId != rows[row].Item.Id) continue;
                    float3 surface = PickUpItemAction.Surface(manager, entity);
                    if (!WorldItemReplacementUtility.MatchesSurface(item.Grounded, item.Instance.Durability, surface, cell, position.y)) continue;
                    float distance = math.lengthsq(surface.xz - new float2(cell.x + .5f,cell.z + .5f));
                    if (distance < best || (distance == best && entity.Index < found.Index)) { found = entity; best = distance; }
                }
                if (found != Entity.Null) { matchedRow = row; return found; }
            }
            return Entity.Null;
        }
    }
}

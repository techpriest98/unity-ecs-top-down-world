using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Items;
using Game.World.Blocks;
using Game.World.Chunks;
using Unity.Mathematics;
using UnityEngine;

namespace Game.World.Saving
{
    public static class WorldStateStorage
    {
        public const int CurrentVersion = 3;

        public static bool TryRead(
            string id,
            uint seed,
            out WorldStateData data,
            out string error)
        {
            data = null;
            error = string.Empty;

            try
            {
                string path = GetPath(id);
                if (!File.Exists(path))
                    return true;

                data = new WorldStateData
                {
                    X = float.NaN,
                    Y = float.NaN,
                    Z = float.NaN,
                    Facing = -1,
                    View = -1,
                    Hour = -1,
                    UpdateTimer = float.NaN,
                    VerticalVelocity = float.NaN
                };

                JsonUtility.FromJsonOverwrite(
                    File.ReadAllText(path), data);

                Validate(data, id, seed);
                return true;
            }
            catch (Exception exception)
            {
                data = null;
                error = "Could not read world state. Check the Console.";
                Debug.LogException(exception);
                return false;
            }
        }

        public static bool TryWrite(
            WorldStateData data,
            out string error)
        {
            error = string.Empty;

            try
            {
                if (data == null)
                    throw new InvalidDataException("World state is missing.");

                Validate(data, data.WorldId, data.Seed);

                string path = GetPath(data.WorldId);
                string directory = Path.GetDirectoryName(path);

                if (!File.Exists(Path.Combine(directory, "world.json")))
                    throw new IOException("World metadata is missing.");

                string temporary = path + ".tmp";
                byte[] bytes = Encoding.UTF8.GetBytes(
                    JsonUtility.ToJson(data));

                using (var stream = new FileStream(
                    temporary,
                    FileMode.Create,
                    FileAccess.Write))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path);

                return true;
            }
            catch (Exception exception)
            {
                error = "Could not save world state. Please try again.";
                Debug.LogException(exception);
                return false;
            }
        }

        private static string GetPath(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _))
                throw new InvalidDataException("Invalid world ID.");

            return Path.Combine(
                WorldMetadataStorage.RootPath,
                id,
                "state.json");
        }

        private static void Validate(
            WorldStateData data,
            string id,
            uint seed)
        {
            if (data == null ||
                data.Version < 1 ||
                data.Version > CurrentVersion ||
                data.WorldId != id ||
                data.Seed != seed ||
                !Finite(data.X) ||
                !Finite(data.Y) ||
                !Finite(data.Z) ||
                !Finite(data.VerticalVelocity) ||
                !Finite(data.UpdateTimer) ||
                data.UpdateTimer < 0f ||
                data.Hour < 0 || data.Hour > 23 ||
                data.Facing < 0 || data.Facing > 3 ||
                data.View < 0 || data.View > 3)
            {
                throw new InvalidDataException(
                    "Invalid or incompatible world state.");
            }

            ValidateEquipment(data);

            // Version 1 has no saved block changes.
            if (data.Version == 1)
            {
                if (data.Chunks != null && data.Chunks.Length != 0)
                {
                    throw new InvalidDataException(
                        "Block changes require state version 2.");
                }

                return;
            }

            if (data.ChunkSizeX != ChunkSettings.SizeX ||
                data.ChunkSizeY != ChunkSettings.SizeY ||
                data.ChunkSizeZ != ChunkSettings.SizeZ ||
                data.Chunks == null)
            {
                throw new InvalidDataException(
                    "Missing block changes or incompatible chunk dimensions.");
            }

            var coordinates = new HashSet<int2>();
            var indices = new HashSet<int>();

            foreach (ChunkChangesData chunk in data.Chunks)
            {
                if (chunk == null ||
                    chunk.Blocks == null ||
                    !coordinates.Add(new int2(chunk.X, chunk.Z)))
                {
                    throw new InvalidDataException(
                        "Invalid or duplicate saved chunk.");
                }

                indices.Clear();

                foreach (BlockChangeData block in chunk.Blocks)
                {
                    if (block == null ||
                        block.Index < 0 ||
                        block.Index >= ChunkSettings.BlockCount ||
                        !indices.Add(block.Index) ||
                        block.BlockId < 0 ||
                        block.BlockId > byte.MaxValue ||
                        !Enum.IsDefined(typeof(BlockId), (BlockId)block.BlockId) ||
                        block.Durability < 0 ||
                        block.Durability > byte.MaxValue)
                    {
                        throw new InvalidDataException(
                            "Invalid or duplicate saved block.");
                    }
                }
            }
        }

        private static void ValidateEquipment(WorldStateData data)
        {
            // Versions 1–2 load with empty equipment.
            if (data.Version < 3)
            {
                if (data.Equipment != null && data.Equipment.Length != 0)
                {
                    throw new InvalidDataException(
                        "Equipment requires state version 3.");
                }

                return;
            }

            if (data.Equipment == null)
            {
                throw new InvalidDataException(
                    "Saved equipment is missing.");
            }

            var slots = new HashSet<int>();

            foreach (EquipmentSlotData entry in data.Equipment)
            {
                if (entry == null)
                {
                    throw new InvalidDataException(
                        "Invalid saved equipment entry.");
                }

                EquipmentSlot slot = (EquipmentSlot)entry.Slot;

                bool singleSlot =
                    entry.Slot > 0 &&
                    (entry.Slot & (entry.Slot - 1)) == 0;

                if (!singleSlot ||
                    !Enum.IsDefined(typeof(EquipmentSlot), slot) ||
                    !slots.Add(entry.Slot))
                {
                    throw new InvalidDataException(
                        "Invalid or duplicate equipment slot.");
                }

                // Compare integer values without narrowing an unknown ID.
                bool knownItem = false;

                foreach (ItemID item in Enum.GetValues(typeof(ItemID)))
                {
                    if (Convert.ToInt32(item) == entry.ItemId)
                    {
                        knownItem = true;
                        break;
                    }
                }

                if (!knownItem)
                {
                    throw new InvalidDataException(
                        "Unknown saved item ID.");
                }
            }
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
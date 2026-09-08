using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.UI
{
    public sealed class WorldCreationValidationResult
    {
        public readonly List<FormError> Errors = new List<FormError>();
        public string Name;
        public uint? Seed;
    }

    public static class WorldCreationValidator
    {
        public static WorldCreationValidationResult Validate(
            WorldCreationForm form, ISet<string> existingNames)
        {
            var result = new WorldCreationValidationResult
            {
                Name = form.WorldName.Trim().Normalize(NormalizationForm.FormC)
            };

            if (string.IsNullOrWhiteSpace(result.Name))
                result.Errors.Add(new FormError(WorldCreationForm.NameId, "Enter a world name."));

            if (existingNames.Contains(result.Name))
                result.Errors.Add(new FormError(WorldCreationForm.NameId,
                    "A world with this name already exists."));

            string seedText = form.Seed.Trim();
            if (seedText.Length > 0)
            {
                if (uint.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed))
                    result.Seed = seed;
                else
                    result.Errors.Add(new FormError(WorldCreationForm.SeedId,
                        "Seed must be between 0 and 4294967295."));
            }

            return result;
        }
    }
}

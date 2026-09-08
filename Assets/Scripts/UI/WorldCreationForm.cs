using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.UI
{
    public sealed class FormError
    {
        public string InputId { get; }
        public string Message { get; }

        public FormError(string inputId, string message)
        {
            InputId = inputId;
            Message = message;
        }
    }

    public sealed class WorldCreationForm
    {
        public const string NameId = "worldName";
        public const string SeedId = "seed";


        private readonly List<FormError> errors = new List<FormError>();
        public ReadOnlyCollection<FormError> Errors { get; }

        public string WorldName { get; private set; } = string.Empty;
        public string Seed { get; private set; } = string.Empty;
        
        public bool HasErrors => errors.Count > 0;
        public event Action Changed;

        public WorldCreationForm()
        {
            Errors = errors.AsReadOnly();
        }

        public void Reset(string name, string seed)
        {
            WorldName = name ?? string.Empty;
            Seed = seed ?? string.Empty;
            errors.Clear();
            Changed?.Invoke();
        }

        public void SetName(string value)
        {
            WorldName = value ?? string.Empty;
            ClearFieldErrors(NameId);
        }

        public void SetSeed(string value)
        {
            Seed = value ?? string.Empty;
            ClearFieldErrors(SeedId);
        }

        public void ClearFieldErrors(string inputId)
        {
            errors.RemoveAll(error => error.InputId == inputId);
            Changed?.Invoke();
        }

        public void ReplaceErrors(IEnumerable<FormError> result)
        {
            // Copy first, so passing the current read-only collection is safe.
            var replacement = new List<FormError>(result);
            errors.Clear();
            errors.AddRange(replacement);
            Changed?.Invoke();
        }

        public string FirstError(string inputId)
        {
            foreach (FormError error in errors)
                if (error.InputId == inputId) return error.Message;
            return string.Empty;
        }
    }
}

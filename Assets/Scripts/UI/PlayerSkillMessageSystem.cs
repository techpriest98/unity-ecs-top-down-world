using Game.Player;
using Game.World.Rendering;
using TMPro;
using Unity.Entities;
using UnityEngine;

namespace Game.UI
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlayerSkillMessageSystem : SystemBase
    {
        private GameObject canvasObject;
        private TextMeshProUGUI label;

        protected override void OnCreate()
        {
            RequireForUpdate<SkillFailureMessage>();
            RequireForUpdate<ViewDirectionComponent>();
        }

        protected override void OnUpdate()
        {
            if (Camera.main == null) { Hide(); return; }
            bool visible = false;
            var direction = SystemAPI.GetSingleton<ViewDirectionComponent>().Value;
            foreach (var (message, position, shape) in SystemAPI.Query<RefRO<SkillFailureMessage>,
                         RefRO<PlayerWorldPosition>, RefRO<PlayerCollisionShape>>().WithAll<PlayerTag>())
            {
                if (message.ValueRO.ExpiresAt <= SystemAPI.Time.ElapsedTime || message.ValueRO.Text.IsEmpty) continue;
                if (canvasObject == null) CreateLabel();
                WorldLabelStyle.EnsureApplied(label);
                var projected = WorldPositionProjectionUtility.Project(position.ValueRO.Value, direction);
                var screen = Camera.main.WorldToScreenPoint(new Vector3(projected.x, projected.y + shape.ValueRO.Height + 0.3f, -1f));
                if (screen.z <= 0) continue;
                label.text = message.ValueRO.Text.ToString();
                float marginX = label.rectTransform.rect.width * .5f + 10f;
                float marginY = label.rectTransform.rect.height * .5f + 4f;
                label.rectTransform.position = new Vector3(
                    Mathf.Clamp(screen.x, marginX, Mathf.Max(marginX, Screen.width - marginX)),
                    Mathf.Clamp(screen.y, marginY, Mathf.Max(marginY, Screen.height - marginY)), 0);
                visible = true;
                break;
            }
            if (canvasObject != null) canvasObject.SetActive(visible);
        }

        private void CreateLabel()
        {
            canvasObject = new GameObject("Player skill message", typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var messagePrefab = Resources.Load<GameObject>("PlayerSkillMessage");
            label = messagePrefab != null
                ? Object.Instantiate(messagePrefab).GetComponent<TextMeshProUGUI>()
                : new GameObject("Speech", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(canvasObject.transform, false);
            label.rectTransform.sizeDelta = new Vector2(480, 88);
            label.alignment = TextAlignmentOptions.Center;
            WorldLabelStyle.Apply(label);
        }

        private void Hide() { if (canvasObject != null) canvasObject.SetActive(false); }
        protected override void OnStopRunning() => Hide();
        protected override void OnDestroy() { if (canvasObject != null) Object.Destroy(canvasObject); }
    }
}

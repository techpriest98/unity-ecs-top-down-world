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
                var projected = WorldPositionProjectionUtility.Project(position.ValueRO.Value, direction);
                var screen = Camera.main.WorldToScreenPoint(new Vector3(projected.x, projected.y + shape.ValueRO.Height + 0.3f, -1f));
                if (screen.z <= 0) continue;
                label.text = message.ValueRO.Text.ToString();
                label.rectTransform.position = new Vector3(
                    Mathf.Clamp(screen.x, 190f, Mathf.Max(190f, Screen.width - 190f)),
                    Mathf.Clamp(screen.y, 36f, Mathf.Max(36f, Screen.height - 36f)), 0);
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
            label.rectTransform.sizeDelta = new Vector2(360, 64);
            label.fontSize = 18;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.764151f, 0.71008366f, 0.71008366f, 1f);
            label.raycastTarget = false;
        }

        private void Hide() { if (canvasObject != null) canvasObject.SetActive(false); }
        protected override void OnStopRunning() => Hide();
        protected override void OnDestroy() { if (canvasObject != null) Object.Destroy(canvasObject); }
    }
}

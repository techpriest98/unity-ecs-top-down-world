using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public sealed class WorldNameValidation : MonoBehaviour
    {
        [SerializeField]
        private TMP_InputField worldNameInput;
        [SerializeField]
        private Button createButton;

        private void OnEnable()
        {
            worldNameInput.onValueChanged.AddListener(UpdateInteractable);
            UpdateInteractable(worldNameInput.text);
        }

        private void OnDisable()
        {
            worldNameInput.onValueChanged.RemoveListener(UpdateInteractable);
        }

        private void UpdateInteractable(string worldName)
        {
            createButton.interactable = !string.IsNullOrWhiteSpace(worldName);
        }
    }
}
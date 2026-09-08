using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Michsky.UI.Heat
{
    public class SettingsDescriptionManager : MonoBehaviour
    {
        [Header("Default Content")]
        [SerializeField] private Sprite cover;
        [SerializeField] private string title = "Settings";
        [SerializeField] [TextArea] private string description = "Description area.";

        [Header("Localization")]
        public string tableID = "UI";
        [SerializeField] private string titleKey;
        [SerializeField] private string descriptionKey;

        [Header("Resources")]
        [SerializeField] private Image coverImage;
        [SerializeField] private TextMeshProUGUI titleObject;
        [SerializeField] private TextMeshProUGUI descriptionObject;

        [Header("Settings")]
        public bool useLocalization = true;

        // Helpers
        [HideInInspector] public LocalizedObject localizedObject;
        private string displayedTitleKey;
        private string displayedDescriptionKey;

        void Awake()
        {
            if (useLocalization && !string.IsNullOrEmpty(titleKey) && !string.IsNullOrEmpty(descriptionKey))
            { 
                CheckForLocalization(); 
            }
        }

        void OnEnable()
        {
            SetDefault();
        }

        void CheckForLocalization()
        {
            localizedObject = gameObject.GetComponent<LocalizedObject>();

            if (localizedObject == null) 
            { 
                localizedObject = gameObject.AddComponent<LocalizedObject>();
                localizedObject.objectType = LocalizedObject.ObjectType.ComponentDriven; 
                localizedObject.updateMode = LocalizedObject.UpdateMode.OnDemand;
                localizedObject.InitializeItem();

                LocalizationSettings locSettings = LocalizationManager.instance.UIManagerAsset.localizationSettings;
                foreach (LocalizationSettings.Table table in locSettings.tables) 
                {
                    if (tableID == table.tableID) 
                    { 
                        localizedObject.tableIndex = locSettings.tables.IndexOf(table);
                        break;
                    }
                }
            }

            if (localizedObject.tableIndex == -1 || LocalizationManager.instance == null || !LocalizationManager.instance.UIManagerAsset.enableLocalization)
            { 
                localizedObject = null;
                useLocalization = false;
            }
            else
            {
                // 현재 가리키는 설정의 설명도 언어 변경 직후 다시 표시합니다.
                localizedObject.localizationKey = titleKey;
                localizedObject.updateMode = LocalizedObject.UpdateMode.OnEnable;
                localizedObject.onLanguageChanged.AddListener(_ => RefreshLocalizedContent());
            }
        }

        public void UpdateLocalizedUI(string newTitleKey, string newDescriptionKey, Sprite newCover)
        {
            displayedTitleKey = newTitleKey;
            displayedDescriptionKey = newDescriptionKey;
            UpdateUI(localizedObject.GetKeyOutput(newTitleKey), localizedObject.GetKeyOutput(newDescriptionKey), newCover);
        }

        private void RefreshLocalizedContent()
        {
            if (localizedObject == null || string.IsNullOrEmpty(displayedTitleKey)) { return; }
            titleObject.text = localizedObject.GetKeyOutput(displayedTitleKey);
            descriptionObject.text = localizedObject.GetKeyOutput(displayedDescriptionKey);
        }

        public void UpdateUI(string newTitle, string newDescription, Sprite newCover) 
        {
            if (newCover != null) { coverImage.sprite = newCover; }
            else { coverImage.sprite = cover; }

            titleObject.text = newTitle;
            descriptionObject.text = newDescription;
        }

        public void SetDefault()
        {
            displayedTitleKey = titleKey;
            displayedDescriptionKey = descriptionKey;
            if (localizedObject == null)
            {
                titleObject.text = title;
                descriptionObject.text = description;
            }

            else
            {
                titleObject.text = localizedObject.GetKeyOutput(titleKey);
                descriptionObject.text = localizedObject.GetKeyOutput(descriptionKey);
            }

            coverImage.sprite = cover;
        }
    }
}

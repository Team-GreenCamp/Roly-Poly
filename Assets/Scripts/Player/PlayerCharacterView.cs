using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerCharacterView : MonoBehaviour
{
    [SerializeField] private GameObject[] characterModels;

    public int CharacterCount
    {
        get
        {
            EnsureCharacterModels();
            return characterModels != null ? characterModels.Length : 0;
        }
    }

    private void Reset()
    {
        AutoAssignCharacterModels();
    }

    private void OnValidate()
    {
        if (characterModels == null || characterModels.Length == 0)
        {
            AutoAssignCharacterModels();
        }
    }

    public void ApplyCharacter(int characterIndex)
    {
        EnsureCharacterModels();

        if (characterModels == null || characterModels.Length == 0)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(characterIndex, 0, characterModels.Length - 1);
        for (int i = 0; i < characterModels.Length; i++)
        {
            if (characterModels[i] != null)
            {
                // 서버가 배정한 캐릭터 하나만 모든 클라이언트에서 보이게 합니다.
                characterModels[i].SetActive(i == safeIndex);
            }
        }
    }

    public Transform GetActiveCharacterRoot()
    {
        EnsureCharacterModels();

        if (characterModels == null || characterModels.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < characterModels.Length; i++)
        {
            if (characterModels[i] != null && characterModels[i].activeSelf)
            {
                // 로비 등장 연출은 현재 선택된 모델 루트에만 적용합니다.
                return characterModels[i].transform;
            }
        }

        return characterModels[0] != null ? characterModels[0].transform : null;
    }

    private void EnsureCharacterModels()
    {
        if (characterModels == null || characterModels.Length == 0)
        {
            AutoAssignCharacterModels();
        }
    }

    private void AutoAssignCharacterModels()
    {
        List<GameObject> candidates = new List<GameObject>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null || !IsCharacterModelRoot(child))
            {
                continue;
            }

            candidates.Add(child.gameObject);
        }

        characterModels = candidates.ToArray();
    }

    private static bool IsCharacterModelRoot(Transform candidate)
    {
        string objectName = candidate.name;
        if (objectName == "CameraRoot" || objectName == "HoldPoint" || objectName == "PlayerNameLabel")
        {
            return false;
        }

        return objectName.StartsWith("SM_Bean")
            || objectName.StartsWith("Character")
            || candidate.GetComponentInChildren<Renderer>(true) != null;
    }
}

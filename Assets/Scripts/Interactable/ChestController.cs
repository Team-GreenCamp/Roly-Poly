using System.Collections;
using UnityEngine;

// ⭐ IInteractable 인터페이스를 상속받아 플레이어가 다가가 E키를 눌렀을 때 상자를 열 수 있도록 만듭니다.
public class ChestController : MonoBehaviour, IInteractable
{
    [Header("상자 연출 설정")]
    [Tooltip("상자의 뚜껑(Lid) 오브젝트를 연결해 주세요.")]
    public Transform lidTransform;

    [Tooltip("상자가 열릴 때 뚜껑이 회전할 로컬 각도 오프셋입니다.")]
    public Vector3 openRotationOffset = new Vector3(-80f, 0f, 0f);

    [Tooltip("뚜껑이 열리는 속도입니다.")]
    public float openSpeed = 2f;

    [Header("열쇠 설정")]
    [Tooltip("상자 내부에 숨겨둘 열쇠 오브젝트(GrabbableObject 등)를 연결해 주세요.")]
    public GameObject keyObject;

    [Header("상호작용 설정")]
    [Tooltip("체크하면 플레이어가 직접 다가가 E키로 상자를 열 수 있습니다.")]
    public bool canDirectInteract = true;

    private bool isOpened = false; // 상자가 열려있는지 상태 저장
    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Coroutine openCoroutine;

    private void Start()
    {
        if (lidTransform != null)
        {
            closedRotation = lidTransform.localRotation;
            openRotation = closedRotation * Quaternion.Euler(openRotationOffset);
        }
        else
        {
            Debug.LogWarning($"🔒 [{gameObject.name}] 상자의 Lid Transform이 할당되지 않았습니다!");
        }

        // 시작 시 상자 안의 열쇠는 자동으로 숨깁니다.
        if (keyObject != null)
        {
            keyObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"🔒 [{gameObject.name}] 상자 내부의 Key Object가 할당되지 않았습니다!");
        }
    }

    // ⭐ 플레이어가 상자를 조작했을 때 실행되는 함수
    public void RequestInteract(GameObject interactor)
    {
        // 직접 열 수 없거나 이미 열린 상자라면 거절
        if (!canDirectInteract || isOpened)
        {
            return;
        }

        OpenChest(interactor);
    }

    // 상자를 여는 핵심 함수
    public void OpenChest(GameObject interactor)
    {
        if (isOpened) return;
        isOpened = true;

        Debug.Log($"📦 {interactor.name}이(가) [{gameObject.name}] 상자를 열었습니다!");

        // 💥 플레이어가 상자 근처에 서 있을 때, 뚜껑이 회전하여 열리면서 플레이어를 밀쳐내 
        // 맵 바깥(지하)으로 추락시키는 물리 버그를 완벽하게 차단합니다. (뚜껑 콜라이더를 임시 트리거로 변환)
        if (lidTransform != null)
        {
            Collider[] lidColliders = lidTransform.GetComponentsInChildren<Collider>(true);
            foreach (var col in lidColliders)
            {
                if (col != null) col.isTrigger = true;
            }
        }

        if (keyObject != null)
        {
            Rigidbody keyRb = keyObject.GetComponent<Rigidbody>();
            if (keyRb != null)
            {
                // 💡 어차피 플레이어가 다가와 E키를 눌러 머리 위로 잡을 오브젝트이므로,
                // 플레이어가 가져가기 전까지는 물리 중력 연산을 켜지 않고 가만히 고정(isKinematic = true)시켜 둡니다.
                keyRb.isKinematic = true;
            }
            keyObject.SetActive(true);
        }

        // 뚜껑 열기 코루틴 시작
        if (lidTransform != null)
        {
            if (openCoroutine != null) StopCoroutine(openCoroutine);
            openCoroutine = StartCoroutine(OpenLidRoutine());
        }
    }

    // 뚜껑을 목표 각도까지 부드럽게 여는 코루틴
    private IEnumerator OpenLidRoutine()
    {
        while (Quaternion.Angle(lidTransform.localRotation, openRotation) > 0.1f)
        {
            lidTransform.localRotation = Quaternion.Slerp(lidTransform.localRotation, openRotation, openSpeed * Time.deltaTime);
            yield return null;
        }
        lidTransform.localRotation = openRotation;
    }
}

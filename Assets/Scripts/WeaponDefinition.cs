using UnityEngine;

[CreateAssetMenu(fileName = "WeaponDefinition", menuName = "BattleRoyal/Weapon Definition")]
public class WeaponDefinition : ScriptableObject
{
    [SerializeField] private string weaponId = "assault_rifle";
    [SerializeField] private string displayName = "Assault Rifle";
    [SerializeField] private GameObject equippedPrefab;
    [SerializeField] private GameObject pickupPrefab;
    [SerializeField] private Vector3 equippedLocalPosition;
    [SerializeField] private Vector3 equippedLocalRotation;
    [SerializeField] private Vector3 equippedLocalScale = Vector3.one;
    [SerializeField] private float damage = 20f;
    [SerializeField] private float fireRate = 8f;
    [SerializeField] private int magazineSize = 30;

    public string WeaponId => weaponId;
    public string DisplayName => displayName;
    public GameObject EquippedPrefab => equippedPrefab;
    public GameObject PickupPrefab => pickupPrefab;
    public Vector3 EquippedLocalPosition => equippedLocalPosition;
    public Vector3 EquippedLocalRotation => equippedLocalRotation;
    public Vector3 EquippedLocalScale => equippedLocalScale;
    public float Damage => damage;
    public float FireRate => fireRate;
    public int MagazineSize => magazineSize;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            weaponId = name.Trim().ToLowerInvariant().Replace(' ', '_');
        }

        damage = Mathf.Max(0f, damage);
        fireRate = Mathf.Max(0.01f, fireRate);
        magazineSize = Mathf.Max(1, magazineSize);
        equippedLocalScale = new Vector3(
            Mathf.Approximately(equippedLocalScale.x, 0f) ? 1f : equippedLocalScale.x,
            Mathf.Approximately(equippedLocalScale.y, 0f) ? 1f : equippedLocalScale.y,
            Mathf.Approximately(equippedLocalScale.z, 0f) ? 1f : equippedLocalScale.z);
    }
}

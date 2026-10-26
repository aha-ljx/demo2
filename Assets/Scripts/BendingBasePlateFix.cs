using UnityEngine;

// The source FBX contains two nearly coplanar base plates. Keep the orange
// "地板_1" plate and hide the duplicate beige "地板" plate at runtime.
public class BendingBasePlateFix : MonoBehaviour
{
    private void Awake()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.name == "地板")
            {
                child.gameObject.SetActive(false);
                return;
            }
        }
    }
}

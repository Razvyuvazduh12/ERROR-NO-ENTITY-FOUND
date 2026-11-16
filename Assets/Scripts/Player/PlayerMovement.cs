using UnityEngine;

public class PlayerMovement : MonoBehaviour
{   
    [SerializeField] public Transform Main_Cam;
    [SerializeField] public float speed = 5f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float horizontal = Input.GetAxis("Horizontal"); // A/D or Left/Right arrows
        float vertical = Input.GetAxis("Vertical");     // W/S or Up/Down arrows

        Vector3 direction = new Vector3(horizontal, 0f, vertical);
        Debug.Log(direction);
    }
}

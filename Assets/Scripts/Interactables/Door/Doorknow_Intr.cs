// Goes on the Doorknob object, sitting right next to its Collider
using UnityEngine;
public class Doorknob_Intr : MonoBehaviour, IInteractable
{
    [SerializeField] private DoorController door;

    public void Interact()
    {
        door.Toggle();
        print('f');
    }
}
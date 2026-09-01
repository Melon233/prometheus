using UnityEngine;

namespace Polyart
{

    public class CharacterClickInteraction : MonoBehaviour
    {
        public Camera playerCamera;
        public float maxDistance = 4f;
        public LayerMask layerMask;

        void Update()
        {
            if (Input.GetMouseButtonDown(0)) // Left click
            {
                FireRay();
            }
        }

        void FireRay()
        {
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, maxDistance, layerMask))
            {
                // Check if the hit object has an IInteractable component
                IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                if (interactable != null)
                {
                    // Create interaction data
                    InteractionData data = new InteractionData
                    {
                        hit = hit,
                        interactor = gameObject // The player interacting
                    };

                    // Call Interact()
                    interactable.Interact(data);
                }
            }

            // Debugging: Draw the ray in the scene view
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.red, 2f);
        }
    }
}
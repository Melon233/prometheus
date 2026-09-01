using System.Collections;
using UnityEngine;

public class ModifyCollisionResponse : MonoBehaviour
{
    public LayerMask includeMask; // Layer to switch to after the first collision
    public LayerMask excludeMask;
    public GameObject particle;
    public GameObject branch;

    private bool hasCollided = false;

    void OnCollisionEnter(Collision collision)
    {
        if (!hasCollided)
        {
            hasCollided = true; // Mark as collided

            // If a new physics material is assigned, apply it
            Collider col = GetComponent<Collider>();
            col.includeLayers = includeMask;
            col.excludeLayers = excludeMask;

            Invoke("DestroyBranch", 2.5f);
        }
    }

    void DestroyBranch()
    { 
        Instantiate(particle, transform.position, transform.rotation);
        Destroy(branch);
    }
}

using UnityEngine;

public class DestroyBark : MonoBehaviour
{
    public GameObject particle;
    public GameObject branch;

    private bool hasCollided = false;
    private string terrainLayerName = "Terrain";

    void OnCollisionEnter(Collision collision)
    {
        if (!hasCollided)
        {
            if (collision.gameObject.layer == LayerMask.NameToLayer(terrainLayerName))
            {
                hasCollided = true;
                Invoke("DestroyBranch", 3.5f);
            }
        }
    }

    void DestroyBranch()
    {
        CapsuleCollider capsuleCollider = GetComponent<CapsuleCollider>();
        Vector3 spawnPosition = transform.TransformPoint(capsuleCollider.center + new Vector3(0,1,0));
        Instantiate(particle, spawnPosition, Quaternion.identity);
        Destroy(branch);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChoppableTree : MonoBehaviour, IInteractable
{
    public GameObject choppedTree;
    public GameObject thisTree;
    public MeshRenderer meshRenderer;
    public GameObject[] branches;
    public ParticleSystem[] fallingParticles;
    public ParticleSystem hitParticle;
    private int hitsToChop = 5;
    private List<Material> materials;
    private Vector3 topCenter;
    private float boundsRadius;
    private bool isOnCooldown = false;
    private float branchSpawnProbability;
    public ChoppableTreeSettings settings;
    public Shader choppableTrunkShader;

    private void Start()
    {
        branchSpawnProbability = settings.branchSpawnProbability;
        hitsToChop = settings.hitsToBreak;

        materials = new List<Material>();
        foreach (Material mat in meshRenderer.sharedMaterials)
        {
            Material tempMat = new Material(mat);
            if (tempMat.shader.name.Contains("Trunk"))
                tempMat.shader = choppableTrunkShader;
            materials.Add(tempMat);
        }
        meshRenderer.SetSharedMaterials(materials);

        topCenter = new Vector3 (thisTree.transform.position.x, meshRenderer.bounds.max.y, thisTree.transform.position.z);
        boundsRadius = Mathf.Max(meshRenderer.localBounds.extents.x, meshRenderer.localBounds.extents.y)/2f;
    }

    private IEnumerator CooldownRoutine(float cooldownTime)
    {
        isOnCooldown = true;
        yield return new WaitForSeconds(cooldownTime);
        isOnCooldown = false;
    }

    public void Interact(InteractionData data)
    {
        if (isOnCooldown)
            return;

        StartCoroutine(CooldownRoutine(1.5f)); // 2 seconds cooldown

        if (--hitsToChop == 0)
        {
            Invoke("Chop", 2f);
        }
        else if (hitsToChop < 0) return;

        UpdateMaterials(data);

        UpdateParticles(data);

        ShakeCamera();

        SpawnBranch();
    }

    void UpdateMaterials(InteractionData data)
    {
        foreach (Material mat in materials)
        {
            mat.SetVector("_PositionAndRadius", new Vector4(data.hit.point.x, data.hit.point.y, data.hit.point.z, 1f));
            mat.SetFloat("_InteractionTime", Time.time + 0.75f);
        }
    }

    void UpdateParticles(InteractionData data)
    {
        Vector3 hitPos = data.hit.point + (data.hit.normal * 0.3f);
        hitParticle.transform.position = hitPos;
        hitParticle.transform.rotation = Quaternion.LookRotation(data.hit.normal);
        hitParticle.Play();

        foreach (var particle in fallingParticles)
        {
            particle.Play();
        }
    }

    void ShakeCamera()
    {
        CameraShake cameraShake = Camera.main.GetComponent<CameraShake>();
        if (cameraShake != null)
        {
            StartCoroutine(cameraShake.Shake(0.15f, 0.3f));
        }
    }

    void SpawnBranch()
    {
        if (branches != null && Random.Range(0f, 1f) < branchSpawnProbability)
        {
            int rand = Random.Range(0, branches.Length);

            Vector3 randomPoint = GetRandomPointAround(topCenter, boundsRadius);
            Quaternion rotation = GetLookAtRotation(topCenter, randomPoint);

            while (GetDotProductXZ(rotation, Camera.main.transform.rotation) < -0.2f)
            {
                randomPoint = GetRandomPointAround(topCenter, boundsRadius);
                rotation = GetLookAtRotation(topCenter, randomPoint);
            }

            randomPoint.y -= 1f;
            Instantiate(branches[rand], randomPoint, rotation);
        }
    }

    void Chop()
    {
        Instantiate(choppedTree, thisTree.transform.position, thisTree.transform.rotation);
        Destroy(thisTree);
    }

    Vector3 GetRandomPointAround(Vector3 origin, float maxRadius)
    {
        float angle = Random.Range(0f, Mathf.PI * 2); // Random angle (0 to 360 degrees)
        float radius = Random.Range(Mathf.Min(1.4f, maxRadius), maxRadius); // Random distance within max radius

        float x = origin.x + Mathf.Cos(angle) * radius; // X coordinate
        float z = origin.z + Mathf.Sin(angle) * radius; // Z coordinate (same height)

        return new Vector3(x, origin.y, z); // Keep the same Y (height)
    }

    Quaternion GetLookAtRotation(Vector3 fromPoint, Vector3 toPoint)
    {
        Vector3 direction = toPoint - fromPoint; // Direction toward the center

        return Quaternion.LookRotation(direction); // Get the rotation facing away
    }
        float GetDotProductXZ(Quaternion rotA, Quaternion rotB)
    {
        // Get forward vectors of both rotations
        Vector3 forwardA = rotA * Vector3.forward;
        Vector3 forwardB = rotB * Vector3.forward;

        // Flatten to XZ plane (ignore Y)
        forwardA.y = 0;
        forwardB.y = 0;

        // Normalize vectors to avoid scale issues
        forwardA.Normalize();
        forwardB.Normalize();

        // Compute and return dot product
        return Vector3.Dot(forwardA, forwardB);
    }
}

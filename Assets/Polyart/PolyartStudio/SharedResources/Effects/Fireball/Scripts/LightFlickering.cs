using UnityEngine;

[ExecuteInEditMode]
public class LightFlickering : MonoBehaviour
{
    private Light currLight;

    public float speed1 = 0.5f, speed2 = 3f;
    public float minIntensity = 0.1f, maxIntensity = 3.8f;
    public float sinOffset = 1.73165f;

    private void Awake()
    {
        currLight = GetComponent<Light>();
    }

    void Update()
    {
        float intensity1 = Mathf.Sin(Time.time * speed1);
        intensity1 += 1f;
        intensity1 *= 0.5f;

        float intensity2 = Mathf.Sin((Time.time + sinOffset) * speed2);
        intensity2 += 1f;
        intensity2 *= 0.5f;

        float intensity = (intensity1 + intensity2) / 2f;

        intensity *= maxIntensity - minIntensity;
        intensity += minIntensity;

        currLight.intensity = intensity;
    }
}

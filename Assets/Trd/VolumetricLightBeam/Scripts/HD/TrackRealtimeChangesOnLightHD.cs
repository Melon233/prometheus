using UnityEngine;

namespace VLB
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light), typeof(VolumetricLightBeamHD))]
    [HelpURL(Consts.Help.HD.UrlTrackRealtimeChangesOnLight)]
    public class TrackRealtimeChangesOnLightHD : MonoBehaviour
    {
        public const string ClassName = "TrackRealtimeChangesOnLightHD";

        VolumetricLightBeamHD m_Master = null;

        void Awake()
        {
            m_Master = GetComponent<VolumetricLightBeamHD>();
            Debug.Assert(m_Master);
        }

        void Update()
        {
            if(m_Master.enabled)
            {
                m_Master.AssignPropertiesFromAttachedSpotLight();
            }
        }
    }
}


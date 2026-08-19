using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;

namespace VLB
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [SelectionBase]
    [HelpURL(Consts.Help.HD.UrlBeam)]
    public partial class VolumetricLightBeamHD2D : VolumetricLightBeamHD
    {
        /// <summary>
        /// Unique ID of the beam's sorting layer.
        /// </summary>
        public int sortingLayerID
        {
            get { return m_SortingLayerID; }
            set {
                m_SortingLayerID = value;
                if (m_BeamGeom) m_BeamGeom.sortingLayerID = value;
            }
        }

        /// <summary>
        /// Name of the beam's sorting layer.
        /// </summary>
        public string sortingLayerName
        {
            get { return SortingLayer.IDToName(sortingLayerID); }
            set { sortingLayerID = SortingLayer.NameToID(value); }
        }

        /// <summary>
        /// The overlay priority within its layer.
        /// Lower numbers are rendered first and subsequent numbers overlay those below.
        /// </summary>
        public int sortingOrder
        {
            get { return m_SortingOrder; }
            set
            {
                m_SortingOrder = value;
                if (m_BeamGeom) m_BeamGeom.sortingOrder = value;
            }
        }

        public override Dimensions GetDimensions() { return Dimensions.Dim2D; }
        public override bool DoesSupportSorting2D() { return true; }
        public override int GetSortingLayerID() { return sortingLayerID; }
        public override int GetSortingOrder() { return sortingOrder; }

        [SerializeField] int m_SortingLayerID = 0;
        [SerializeField] int m_SortingOrder = 0;

#if UNITY_EDITOR
        public override void Reset()
        {
            base.Reset();

            sortingLayerID = 0;
            sortingOrder = 0;
        }
#endif // UNITY_EDITOR
    }
}

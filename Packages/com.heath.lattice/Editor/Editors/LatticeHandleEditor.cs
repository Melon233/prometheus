using System;
using System.Linq;
using UnityEditor;

namespace Lattice.Editor
{
	/// <summary>
	/// Custom editor that selects the owning Lattices and the equivalent gizmo handles
	/// </summary>
	[CustomEditor(typeof(LatticeHandle)), CanEditMultipleObjects]
	public class LatticeHandleEditor : UnityEditor.Editor
	{
		internal static LatticeHandle[] Selected = Array.Empty<LatticeHandle>();

		private void OnEnable()
		{
			Selected = targets.Cast<LatticeHandle>().ToArray();
			Selection.objects = Selected.Select(t => t.transform.parent.gameObject).ToArray();
		}
	}
}

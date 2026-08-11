using UnityEngine;
using UnityEngine.Rendering;

namespace Lattice.Editor
{
	/// <summary>
	/// Methods for baking lattice components
	/// </summary>
	internal static class LatticeBaker
	{
		private static readonly CommandBuffer _cmd = new();
		private static readonly BakeLookup _lookup = new();

		internal static void Clear()
		{
			_lookup.Clear();
		}

		internal static void Bake(LatticeModifier modifier, bool lightmapping = false)
		{
			if (!modifier.isActiveAndEnabled) return;
			if (!modifier.TryGetComponent(out MeshFilter filter)) return;

			if (!_lookup.TryGet(modifier, out Mesh deformedMesh))
			{
				_cmd.Clear();
				LatticeFeature.ApplyModifier(_cmd, modifier);
				Graphics.ExecuteCommandBuffer(_cmd);

				deformedMesh = modifier.GetDeformedMesh();
				_lookup.Add(modifier, deformedMesh);
			}

			if (!lightmapping)
			{
				modifier.enabled = false;

				if (modifier.TryGetComponent(out MeshCollider collider) &&
					(collider.sharedMesh == modifier.TargetMesh))
				{
					collider.sharedMesh = deformedMesh;
				}
			}

			filter.sharedMesh = deformedMesh;
		}

		internal static void Bake(TransformLatticeModifier modifier)
		{
			if (!modifier.isActiveAndEnabled) return;

			modifier.Apply();

			Vector3 position = modifier.transform.position;
			Quaternion rotation = modifier.transform.rotation;
			Vector3 localScale = modifier.transform.localScale;

			modifier.enabled = false;
			modifier.transform.SetPositionAndRotation(position, rotation);
			modifier.transform.localScale = localScale;
		}
	}
}

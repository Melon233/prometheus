using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lattice.Editor
{
	/// <summary>
	/// Callback class that bakes any modifiers/lattices marked as static when the scene is being built
	/// </summary>
	internal class LatticeProcessScene : IProcessSceneWithReport
	{
		public int callbackOrder => 0;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			LatticeBaker.Clear();

			foreach (GameObject rootObject in scene.GetRootGameObjects())
			{
				// Bake static lattice modifiers
				LatticeModifier[] modifiers = rootObject.GetComponentsInChildren<LatticeModifier>();
				foreach (LatticeModifier modifier in modifiers)
				{
					if (modifier.gameObject.isStatic)
						LatticeBaker.Bake(modifier);
				}

				// Bake static transform modifiers
				TransformLatticeModifier[] transforms = rootObject.GetComponentsInChildren<TransformLatticeModifier>();
				foreach (TransformLatticeModifier modifier in transforms)
				{
					if (modifier.gameObject.isStatic)
						LatticeBaker.Bake(modifier);
				}

				// Disable static lattices
				Lattice[] lattices = rootObject.GetComponentsInChildren<Lattice>();
				foreach (Lattice lattice in lattices)
				{
					if (lattice.gameObject.isStatic)
						lattice.enabled = false;
				}
			}
		}
	}
}

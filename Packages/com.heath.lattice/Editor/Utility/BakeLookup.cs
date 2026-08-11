using System.Collections.Generic;
using UnityEngine;

namespace Lattice.Editor
{
	/// <summary>
	/// A dictionary for storing deformed meshes, and a lookup for similar/identical modifiers
	/// </summary>
	internal class BakeLookup
	{
		private const float Threshold = 0.0001f; // Minimum difference in floats

		private readonly Dictionary<Mesh, List<Bake>> _bakedMeshes = new();

		internal void Clear()
		{
			foreach ((Mesh mesh, List<Bake> bakes) in _bakedMeshes)
			{
				foreach (Bake bake in bakes)
				{
					Object.DestroyImmediate(bake.DeformedMesh);
				}
			}
			_bakedMeshes.Clear();
		}

		internal bool TryGet(LatticeModifier modifier, out Mesh mesh)
		{
			mesh = null;

			if (!_bakedMeshes.TryGetValue(modifier.TargetMesh, out List<Bake> bakes))
				return false;

			BakedSettings bakeSettings = new(modifier);

			foreach (Bake bake in bakes)
			{
				if (bakeSettings.Equals(bake.Settings))
				{
					mesh = bake.DeformedMesh;
					return true;
				}
			}

			return false;
		}

		internal void Add(LatticeModifier modifier, Mesh mesh)
		{
			Bake bake = new()
			{
				Settings = new(modifier),
				DeformedMesh = mesh,
			};

			if (!_bakedMeshes.TryGetValue(modifier.TargetMesh, out List<Bake> bakes))
			{
				bakes = new();
				_bakedMeshes.Add(modifier.TargetMesh, bakes);
			}

			bakes.Add(bake);
		}

		/// <summary>
		/// A baked mesh and the lattice settings that were used
		/// </summary>
		private struct Bake
		{
			public BakedSettings Settings;
			public Mesh DeformedMesh;

			public bool Equals(Bake bake)
			{
				return Settings.Equals(bake.Settings);
			}
		}

		/// <summary>
		/// The modifier and lattices settings which were used for baking
		/// </summary>
		private struct BakedSettings
		{
			public ApplyMethod ApplyMethod;
			public TextureCoordinate StretchChannel;
			public List<BakedLattice> Lattices;

			public BakedSettings(LatticeModifier modifier)
			{
				ApplyMethod = modifier.ApplyMethod;
				StretchChannel = modifier.StretchChannel;
				Lattices = new();
				foreach (LatticeItem item in modifier.Lattices)
				{
					Lattices.Add(new BakedLattice(modifier, item));
				}
			}

			public bool Equals(BakedSettings modifier)
			{
				// Check modifier settings are equal
				if ((ApplyMethod != modifier.ApplyMethod) ||
					(StretchChannel != modifier.StretchChannel))
					return false;

				// Check lattices are equal
				if (Lattices.Count != modifier.Lattices.Count)
					return false;
				for (int i = 0; i < Lattices.Count; i++)
				{
					if (!Lattices[i].Equals(modifier.Lattices[i]))
						return false;
				}

				return true;
			}
		}

		/// <summary>
		/// Lattice settings used when baking
		/// </summary>
		private struct BakedLattice
		{
			public LatticeItem Item;
			public Matrix4x4 ModifierToLattice;
			public Vector3Int Resolution;
			public Vector3[] HandleOffsets;

			public BakedLattice(LatticeModifier modifier, LatticeItem item)
			{
				Lattice lattice = item.Lattice;

				Item = item;
				ModifierToLattice = lattice.transform.worldToLocalMatrix *
					modifier.transform.localToWorldMatrix;
				Resolution = lattice.Resolution;
				HandleOffsets = lattice.Offsets.ToArray();
			}

			public bool Equals(BakedLattice lattice)
			{
				// Check resolution is equal
				if (Resolution != lattice.Resolution)
					return false;

				// Check items are equal
				if ((Item.Global != lattice.Item.Global) ||
					(Item.Interpolation != lattice.Item.Interpolation) ||
					!Item.Mask.Equals(lattice.Item.Mask))
					return false;

				// Check handle offsets are equal
				if (HandleOffsets.Length != lattice.HandleOffsets.Length)
					return false;
				for (int i = 0; i < HandleOffsets.Length; i++)
				{
					float distance = (HandleOffsets[i] - lattice.HandleOffsets[i]).sqrMagnitude;
					if (distance > Threshold)
						return false;
				}

				// Check matrices are equal
				for (int i = 0; i < 4; i++)
				{
					for (int j = 0; j < 4; j++)
					{
						float distance = Mathf.Abs(ModifierToLattice[i, j] - lattice.ModifierToLattice[i, j]);
						if (distance > Threshold)
							return false;
					}
				}

				return true;
			}
		}
	}
}

using System;
using UnityEditor;
using UnityEngine;

namespace Lattice.Editor
{
	/// <summary>
	/// Utility class for drawing gizmos to move, rotate and scale handle selections
	/// </summary>
	public class HandleGizmos : IDisposable
	{
		public enum Action
		{
			None,
			Moving,
			Rotating,
			Scaling
		};

		private readonly Lattice _lattice;
		private readonly SelectedHandles _handles;

		private Action _action = Action.None;
		private bool _refresh = false;

		private Array3D<float> _weights = new();
		private Array3D<Vector3> _originalPositions = new();

		private Vector3 _originalPosition = Vector3.zero;
		private Quaternion _originalRotation = Quaternion.identity;
		private Vector3 _originalScale = Vector3.one;
		private Matrix4x4 _originalMatrix = Matrix4x4.identity;
		private Matrix4x4 _originalInverse = Matrix4x4.identity;

		private Vector3 _previousToolPosition = Vector3.zero;
		private Quaternion _previousToolRotation = Quaternion.identity;
		private Vector3 _previousToolScale = Vector3.one;

		private float _currentAngle = 0;
		private Vector3 _currentAxis = Vector3.zero;

		public HandleGizmos(Lattice lattice, SelectedHandles handles)
		{
			_lattice = lattice;
			_handles = handles;
			_handles.SelectionChanged += Reset;

			Reset();
		}

		public void Dispose()
		{
			_handles.SelectionChanged -= Reset;
		}

		public void Draw()
		{
			Event current = Event.current;

			// Handle input events
			if (current.type == EventType.MouseDown) OnMouseDown();
			else if (current.rawType == EventType.MouseUp) OnMouseUp();
			else if (current.type == EventType.ScrollWheel) OnScroll();
			else if (_action == Action.None) Reset();

			DrawFalloff();

			// Draw currently selected tool
			if (Tools.current == Tool.Move) DrawPositionGizmo();
			else if (Tools.current == Tool.Rotate) DrawRotationGizmo();
			else if (Tools.current == Tool.Scale) DrawScaleGizmo();

			_refresh = false;
		}

		public void Reset()
		{
			_originalPosition = _handles.GetPivot(Tools.pivotMode);
			_originalRotation = (Tools.pivotRotation == PivotRotation.Global)
				? Quaternion.identity
				: _lattice.transform.rotation;
			_originalScale = Vector3.one;

			// If using selection relative gizmos, we use lattice deformation to
			// estimate the rotation at that center of selected handles
			if (LatticeSettings.SelectionRelativeGizmos)
			{
				Vector3 pivot = _handles.GetBasePivot(PivotMode.Center);

				LatticeItem item = new()
				{
					Lattice = _lattice,
					Interpolation = (_handles.Count > 1) 
						? InterpolationMethod.LinearSharp 
						: InterpolationMethod.Cubic,
					Global = true,
				};

				Matrix4x4 deformed = LatticeSolver.DeformTransform(item, 
					Matrix4x4.TRS(pivot, _lattice.transform.rotation, Vector3.one));

				_originalRotation = deformed.rotation;
			}

			_originalMatrix = Matrix4x4.TRS(_originalPosition, _originalRotation, Vector3.one);
			_originalInverse = _originalMatrix.inverse;

			_previousToolPosition = _originalPosition;
			_previousToolRotation = _originalRotation;
			_previousToolScale = _originalScale;

			_action = Action.None;
			_currentAngle = 0;
			_currentAxis = Vector3.zero;
		}

		private void OnMouseDown()
		{
			Reset();

			// Store current positions
			_originalPositions.Resize(_lattice.Resolution);
			foreach (Vector3Int coords in _lattice.GetHandles())
			{
				_originalPositions[coords] = _lattice.GetHandleWorldPosition(coords);
			}

			// Calculate weights
			CalculateWeights();
		}

		private void OnMouseUp()
		{
			if (_action != Action.None)
			{
				foreach (Vector3Int coords in _lattice.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 newPosition = _lattice.GetHandleWorldPosition(coords);

					if (Vector3.Distance(newPosition, originalPosition) > 0.00001f)
					{
						LatticeHandle handle = _lattice.GetHandle(coords);

						_lattice.SetHandleWorldPosition(coords, originalPosition);
						Undo.RecordObject(handle, "Moved Lattice Handles");
						_lattice.SetHandleWorldPosition(coords, newPosition);
					}
				}
			}

			Reset();
		}

		private void OnScroll()
		{
			if (_action == Action.None) return;

			float scale = (Event.current.delta.y > 0) ? 1.15f : 1 / 1.15f;
			LatticeSettings.SelectionFalloffRadius *= scale;
			CalculateWeights();

			Event.current.Use();
			_refresh = true;
		}

		private void DrawFalloff()
		{
			if (!LatticeSettings.SelectionFalloffEnabled || _action == Action.None) return;

			using var scope = new Handles.DrawingScope(LatticeSettings.SelectionFalloffColor);

			float size = LatticeSettings.SelectionFalloffRadius * HandleUtility.GetHandleSize(_originalPosition);
			Vector3 forward = SceneView.currentDrawingSceneView.camera.transform.forward;
			Handles.DrawWireDisc(_originalPosition, forward, size, 1f);
		}

		private void CalculateWeights()
		{
			float radius = LatticeSettings.SelectionFalloffRadius * HandleUtility.GetHandleSize(_originalPosition);

			_weights.Resize(_originalPositions.Size);

			foreach (Vector3Int handle in _handles.Handles)
			{
				if (LatticeSettings.SelectionFalloffEnabled)
				{
					Vector3 position = _originalPositions[handle];

					foreach (Vector3Int coords in _lattice.GetHandles())
					{
						float distance = (_originalPositions[coords] - position).magnitude;
						float weight = LatticeSettings.SelectionFalloffCurve.Evaluate(distance / radius);

						if (weight > _weights[coords])
						{
							_weights[coords] = weight;
						}
					}
				}
				else
				{
					_weights[handle] = 1;
				}
			}
		}

		private void DrawPositionGizmo()
		{
			EditorGUI.BeginChangeCheck();

			_previousToolPosition = Handles.PositionHandle(_previousToolPosition, _originalRotation);

			if (EditorGUI.EndChangeCheck() || ((_action == Action.Moving) && _refresh))
			{
				_action = Action.Moving;

				Vector3 offset = _previousToolPosition - _originalPosition;

				foreach (Vector3Int coords in _lattice.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 newPosition = Vector3.Lerp(originalPosition, originalPosition + offset, _weights[coords]);
					_lattice.SetHandleWorldPosition(coords, newPosition);
				}

				EditorApplication.QueuePlayerLoopUpdate();
			}
		}

		private void DrawRotationGizmo()
		{
			EditorGUI.BeginChangeCheck();

			_previousToolRotation = Handles.RotationHandle(_previousToolRotation, _originalPosition);

			if (EditorGUI.EndChangeCheck() || ((_action == Action.Rotating) && _refresh))
			{
				_action = Action.Rotating;

				float rotationAngle = 0;
				Vector3 rotationAxis = Vector3.zero;

				Quaternion relativeRotation = Quaternion.Inverse(_originalRotation) * _previousToolRotation;

				// Convert to angle axis
				if (relativeRotation != Quaternion.identity)
				{
					relativeRotation.ToAngleAxis(out rotationAngle, out rotationAxis);

					if (_currentAxis == Vector3.zero) _currentAxis = rotationAxis;
				}

				if (_currentAxis == Vector3.zero) return;

				// Flip amount of rotation if axis has flipped
				if (Vector3.Dot(rotationAxis, _currentAxis) < 0)
				{
					rotationAngle = 360 - rotationAngle;
				}

				// Get the delta and modulo
				float delta = rotationAngle - _currentAngle;
				delta = (((delta + 180.0f) % 360.0f) + 360.0f) % 360.0f - 180.0f;

				// Add delta to current rotation amount
				_currentAngle += delta;

				foreach (Vector3Int coords in _lattice.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 relativePosition = originalPosition - _originalPosition;

					float weightedAngle = Mathf.Lerp(0, _currentAngle, _weights[coords]);
					Quaternion rotation = Quaternion.AngleAxis(weightedAngle, _currentAxis);

					rotation = _originalRotation * rotation * Quaternion.Inverse(_originalRotation);

					relativePosition = rotation * relativePosition;

					Vector3 newPosition = relativePosition + _originalPosition;
					_lattice.SetHandleWorldPosition(coords, newPosition);
				}

				EditorApplication.QueuePlayerLoopUpdate();
			}
		}

		private void DrawScaleGizmo()
		{
			EditorGUI.BeginChangeCheck();

			_previousToolScale = Handles.ScaleHandle(_previousToolScale, _originalPosition, _originalRotation);

			if (EditorGUI.EndChangeCheck() || (_action == Action.Scaling && _refresh))
			{
				_action = Action.Scaling;

				Vector3 change = new(
					_previousToolScale.x / _originalScale.x,
					_previousToolScale.y / _originalScale.y,
					_previousToolScale.z / _originalScale.z
				);

				foreach (Vector3Int coords in _lattice.GetHandles())
				{
					Vector3 originalPosition = _originalPositions[coords];
					Vector3 relativePosition = originalPosition - _originalPosition;

					relativePosition = _originalInverse.MultiplyVector(relativePosition);

					relativePosition.Scale(change);

					relativePosition = _originalMatrix.MultiplyVector(relativePosition);

					Vector3 newPosition = Vector3.Lerp(originalPosition, relativePosition + _originalPosition, _weights[coords]);
					_lattice.SetHandleWorldPosition(coords, newPosition);
				}

				EditorApplication.QueuePlayerLoopUpdate();
			}
		}
	}
}

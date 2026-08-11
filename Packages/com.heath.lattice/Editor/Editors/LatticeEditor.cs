using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace Lattice.Editor
{
	/// <summary>
	/// Editor for <see cref="Lattice"/>. <br/>
	/// Displays lattices in the scene and provides tools for editing them.
	/// </summary>
	[CustomEditor(typeof(Lattice))]
	public class LatticeEditor : UnityEditor.Editor
	{
		private bool _frameBounds = false;

		private SelectedHandles _selectedHandles;
		private LatticeGizmos _latticeGizmos;
		private LatticeDrawer _latticeDrawer;
		private HandleGizmos _handleGizmos;
		private HandleDrawer _handleDrawer;

		private Lattice Lattice => target as Lattice;

		/// <summary>
		/// Callback to draw lattice when it's selected in the hierarchy
		/// </summary>
		[DrawGizmo(GizmoType.InSelectionHierarchy, typeof(Lattice))]
		internal static void OnDrawGizmo(Lattice lattice, GizmoType gizmoType)
		{
			// Don't draw currently selected lattice, that's handled by OnSceneGUI
			if (gizmoType.HasFlag(GizmoType.Active) && Selection.count == 1) return;

			// Draw lattices in hierarchy
			LatticeDrawer.Draw(lattice);
		}

		/// <summary>
		/// Creates a new Lattice in the scene
		/// </summary>
		[MenuItem("GameObject/Effects/Lattice", false, 4040)]
		private static void CreateLattice(MenuCommand menuCommand)
		{
			// Don't run if multiple selected objects
			if ((menuCommand.context != null) && (menuCommand.context != Selection.activeGameObject))
				return;

			GameObject gameObject = new("Lattice");
			Undo.RegisterCreatedObjectUndo(gameObject, "Create Lattice");

			// If just one selected, add as a child
			if (Selection.gameObjects.Length == 1)
			{
				Undo.SetTransformParent(gameObject.transform, Selection.activeTransform, "Create Lattice");
				gameObject.transform.localPosition = Vector3.zero;
				gameObject.transform.localRotation = Quaternion.identity;
				gameObject.transform.localScale = Vector3.one;
			}
			// Otherwise root object in centre of screen
			else if (SceneView.lastActiveSceneView != null)
			{
				gameObject.transform.localPosition = SceneView.lastActiveSceneView.pivot;
			}

			// Setup the lattice
			Undo.RegisterCompleteObjectUndo(gameObject, "Create Lattice");
			Lattice lattice = Undo.AddComponent<Lattice>(gameObject);
			lattice.Setup(lattice.Resolution);

			void AddLatticeToList(List<LatticeItem> items)
			{
				LatticeItem item = new() { Lattice = lattice };

				if (items.Count > 0)
				{
					LatticeItem previous = items[^1];
					item.Interpolation = previous.Interpolation;

					if (previous.Lattice == null)
					{
						items[^1] = item;
						return;
					}
				}

				items.Add(item);
			}

			// Add lattice to any modifiers selected
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				if (Selection.gameObjects[i].TryGetComponent(out LatticeModifierBase modifier))
				{
					Undo.RegisterCompleteObjectUndo(modifier, "Create Lattice");
					AddLatticeToList(modifier.Lattices);
				}
				if (Selection.gameObjects[i].TryGetComponent(out TransformLatticeModifier transformModifier))
				{
					Undo.RegisterCompleteObjectUndo(transformModifier, "Create Lattice");
					AddLatticeToList(transformModifier.Lattices);
				}
			}

			Undo.SetCurrentGroupName("Create Lattice");

			Selection.activeTransform = gameObject.transform;
		}

		#region Editor

		public override void OnInspectorGUI()
		{
			Vector3Int initialResolution = Lattice.Resolution;

			base.OnInspectorGUI();

			using (var disabled = new EditorGUI.DisabledGroupScope(true))
			{
				if (_selectedHandles.Count == 0) EditorGUILayout.LabelField("Selected: None");
				else if (_selectedHandles.Count == 1) EditorGUILayout.LabelField($"Selected: {_selectedHandles.Handles[0]}");
				else EditorGUILayout.LabelField($"Selected: {_selectedHandles.Count} Total");
			}

			if (Lattice.Resolution != initialResolution)
			{
				Lattice.Setup(Lattice.Resolution);
				Undo.RecordObject(_selectedHandles, "Changed Lattice Resolution");
				_selectedHandles.Clear();
				Undo.SetCurrentGroupName("Changed Lattice Resolution");
			}
		}

		private void OnSceneGUI()
		{
			// Draw lattice
			_latticeDrawer.Draw();

			// If other objects also selected return early
			if (!ShouldShowEditor()) return;

			UpdateSelection();

			// Draw handles
			_handleDrawer.Draw(!_selecting);

			// Draw appropriate gizmos
			if (_selectedHandles.Count > 0) _handleGizmos.Draw();
			else _latticeGizmos.Draw();

			// Handle mouse events
			HandleSelection();
			HandleMouseEvents();
		}

		private void OnEnable()
		{
			_selectedHandles = SelectedHandles.Get(Lattice);
			_latticeGizmos = new LatticeGizmos(Lattice);
			_latticeDrawer = new LatticeDrawer(Lattice);
			_handleGizmos = new HandleGizmos(Lattice, _selectedHandles);
			_handleDrawer = new HandleDrawer(Lattice, _selectedHandles);

			_selectedHandles.SelectionChanged += ResetFocus;
			Tools.pivotRotationChanged += ResetGizmos;
			Tools.viewToolChanged += ResetGizmos;
			Undo.undoRedoPerformed += ResetGizmos;
			Selection.selectionChanged += OnSelectionChanged;

			OnSelectionChanged();
		}

		private void OnDisable()
		{
			_handleGizmos.Dispose();

			_selectedHandles.SelectionChanged -= ResetFocus;
			Tools.pivotRotationChanged -= ResetGizmos;
			Tools.viewToolChanged -= ResetGizmos;
			Undo.undoRedoPerformed -= ResetGizmos;
			Selection.selectionChanged -= OnSelectionChanged;

			Tools.hidden = false;
		}

		private bool HasFrameBounds() => true;

		private Bounds OnGetFrameBounds()
		{
			_frameBounds = !_frameBounds;

			if (_selectedHandles.Count > 0)
			{
				if (_frameBounds) return _selectedHandles.GetBounds();
				else return new(_selectedHandles.GetPivot(Tools.pivotMode), Vector3.one);
			}
			else
			{
				// Gets bounds of handles
				Vector3 initial = Lattice.GetHandleWorldPosition(0, 0, 0);
				Bounds bounds = new(initial, Vector3.zero);
				foreach (Vector3Int handle in Lattice.GetHandles())
				{
					bounds.Encapsulate(Lattice.GetHandleWorldPosition(handle));
				}

				// Focus on bounds or pivot
				Vector3 pivot = bounds.center;
				return _frameBounds ? bounds : new(pivot, Vector3.one);
			}
		}

		private void ResetFocus()
		{
			_frameBounds = false;
			Repaint();
		}

		private void ResetGizmos()
		{
			_latticeGizmos.Reset();
			_handleGizmos.Reset();
		}

		private void OnSelectionChanged()
		{
			Tools.hidden = ShouldShowEditor();
		}

		private bool ShouldShowEditor()
		{
			return Selection.count == 1;
		}

		private void UpdateSelection()
		{
			if (LatticeHandleEditor.Selected.Length > 0)
			{
				Undo.RecordObject(_selectedHandles, "Select Lattice Handles");
				_selectedHandles.Clear();
				_selectedHandles.AddRange(LatticeHandleEditor.Selected.Select(h => Lattice.GetHandleCoords(h)));
				LatticeHandleEditor.Selected = System.Array.Empty<LatticeHandle>();
			}
		}

		#endregion

		#region Mouse Events

		private static readonly Vector3Int[] Directions = new[]
		{
			Vector3Int.right, Vector3Int.left,
			Vector3Int.up, Vector3Int.down,
			Vector3Int.forward, Vector3Int.back,
		};

		private static readonly Color SelectionFaceColor = new(0.1f, 0.4f, 1f, 0.05f);
		private static readonly Color SelectionOutlineColor = new(0.1f, 0.4f, 1f, 0.2f);

		private bool _selecting = false;
		private Vector2 _selectingStartPos = Vector2.zero;
		private readonly HashSet<Vector3Int> _handlesWithinSelection = new();

		private Vector2 _rightMouseDownPos = Vector2.zero;

		private void HandleSelection()
		{
			if (_selecting)
			{
				Rect selectionRect = Rect.MinMaxRect(
					_selectingStartPos.x,
					_selectingStartPos.y,
					Event.current.mousePosition.x,
					Event.current.mousePosition.y
				);

				// Add new handles
				_handlesWithinSelection.Clear();
				foreach (Vector3Int handle in Lattice.GetHandles())
				{
					Vector3 handlePosition = Lattice.GetHandleWorldPosition(handle);
					Vector2 guiPosition = HandleUtility.WorldToGUIPoint(handlePosition);

					if (selectionRect.Contains(guiPosition, true))
					{
						_handlesWithinSelection.Add(handle);
					}
				}

				// Draw selection rectangle
				Handles.BeginGUI();
				Handles.DrawSolidRectangleWithOutline(selectionRect, SelectionFaceColor, SelectionOutlineColor);
				Handles.EndGUI();

				EditorApplication.QueuePlayerLoopUpdate();
			}
		}

		private void HandleMouseEvents()
		{
			// Update scene view when moving mouse
			if (Event.current.type == EventType.MouseMove) SceneView.RepaintAll();

			// Add default control
			Event current = Event.current;
			int controlId = GUIUtility.GetControlID(FocusType.Passive);
			HandleUtility.AddDefaultControl(controlId);

			// Left mouse
			if (current.button == 0)
			{
				// If mouse down then start selecting
				if (current.GetTypeForControl(controlId) == EventType.MouseDown && !current.alt)
				{
					GUIUtility.hotControl = controlId;

					_selecting = true;
					_selectingStartPos = current.mousePosition;
					current.Use();
				}

				// If mouse up then stop selecting
				if (current.GetTypeForControl(controlId) == EventType.MouseUp && _selecting)
				{
					GUIUtility.hotControl = 0;

					// Deselect object if no selection made and didn't previously have a handle selected
					if (_handlesWithinSelection.Count == 0 && _selectedHandles.Count == 0 && !current.shift && !current.alt)
					{
						Selection.activeGameObject = null;
						_selecting = false;
						current.Use();
						return;
					}

					// If holding shift xor with current selection
					if (current.shift)
					{
						_handlesWithinSelection.SymmetricExceptWith(_selectedHandles.Handles);
					}

					// Update selection
					Undo.RecordObject(_selectedHandles, "Select Lattice Handles");
					_selectedHandles.Clear();
					_selectedHandles.AddRange(_handlesWithinSelection);
					_selecting = false;
					current.Use();
				}
			}

			// Right mouse
			if (current.button == 1)
			{
				// If mouse down
				if (current.GetTypeForControl(controlId) == EventType.MouseDown)
				{
					// Record mouse position
					_rightMouseDownPos = current.mousePosition;
				}

				// If mouse up
				if (current.GetTypeForControl(controlId) == EventType.MouseUp)
				{
					// If in the same position as when mouse down
					if (current.mousePosition == _rightMouseDownPos)
					{
						// Show context menu
						ShowContextMenu();
						current.Use();
					}
				}
			}
		}

		private Vector3Int ClampHandle(Vector3Int handle)
		{
			return Vector3Int.Min(Vector3Int.Max(handle, Vector3Int.zero), Lattice.Resolution - Vector3Int.one);
		}

		private void ShowContextMenu()
		{
			GenericMenu menu = new();

			// Selection falloff
			menu.AddItem(new GUIContent("Use Selection Falloff"), LatticeSettings.SelectionFalloffEnabled, () =>
			{
				LatticeSettings.SelectionFalloffEnabled = !LatticeSettings.SelectionFalloffEnabled;
			});

			// Selection relative gizmos
			menu.AddItem(new GUIContent("Use Selection Relative Gizmos"), LatticeSettings.SelectionRelativeGizmos, () =>
			{
				LatticeSettings.SelectionRelativeGizmos = !LatticeSettings.SelectionRelativeGizmos;
			});

			menu.AddSeparator("");

			// Expand and shrink
			if (_selectedHandles.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent("Expand Selection"));
				menu.AddDisabledItem(new GUIContent("Shrink Selection"));
			}
			else
			{
				menu.AddItem(new GUIContent("Expand Selection"), false, () =>
				{
					Undo.RecordObject(_selectedHandles, "Expand Selection");
					List<Vector3Int> handlesToAdd = new();
					foreach (Vector3Int handle in Lattice.GetHandles())
					{
						if (Directions.Any(direction => _selectedHandles.Contains(ClampHandle(handle + direction))))
						{
							handlesToAdd.Add(handle);
						}
					}

					foreach (Vector3Int handle in handlesToAdd)
					{
						_selectedHandles.Add(handle);
					}
				});

				menu.AddItem(new GUIContent("Shrink Selection"), false, () =>
				{
					Undo.RecordObject(_selectedHandles, "Shrink Selection");
					List<Vector3Int> handlesToRemove = new();
					foreach (Vector3Int handle in Lattice.GetHandles())
					{
						if (!Directions.All(direction => _selectedHandles.Contains(ClampHandle(handle + direction))))
						{
							handlesToRemove.Add(handle);
						}
					}

					foreach (Vector3Int handle in handlesToRemove)
					{
						_selectedHandles.Remove(handle);
					}
				});
			}

			// Invert selection
			menu.AddItem(new GUIContent("Invert Selection"), false, () =>
			{
				Undo.RecordObject(_selectedHandles, "Invert Selection");
				foreach (Vector3Int handle in Lattice.GetHandles())
				{
					if (_selectedHandles.Contains(handle)) _selectedHandles.Remove(handle);
					else _selectedHandles.Add(handle);
				}
			});

			menu.AddSeparator("");

			// Select all handles
			menu.AddItem(new GUIContent("Select All Handles"), false, () =>
			{
				Undo.RecordObject(_selectedHandles, "Select All Handles");
				foreach (Vector3Int handle in Lattice.GetHandles())
				{
					_selectedHandles.Add(handle);
				}
			});

			// Select exterior handles
			menu.AddItem(new GUIContent("Select Exterior Handles"), false, () =>
			{
				Undo.RecordObject(_selectedHandles, "Select Exterior Handles");
				foreach (Vector3Int handle in Lattice.GetHandles())
				{
					if (handle.x == 0 || handle.y == 0 || handle.z == 0 ||
						(handle.x == (Lattice.Resolution.x - 1)) ||
						(handle.y == (Lattice.Resolution.y - 1)) ||
						(handle.z == (Lattice.Resolution.z - 1)))
					{
						_selectedHandles.Add(handle);
					}
				}
			});

			menu.AddSeparator("");

			// Reset selected handles
			if (_selectedHandles.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent("Reset Selected Handles"));
			}
			else
			{
				menu.AddItem(new GUIContent("Reset Selected Handles"), false, () =>
				{
					foreach (Vector3Int handle in _selectedHandles.Handles)
					{
						Undo.RecordObject(Lattice.GetHandle(handle), "Reset Handles");
						Lattice.SetHandleOffset(handle, Vector3.zero);
					}
					_handleGizmos.Reset();
					EditorApplication.QueuePlayerLoopUpdate();
				});
			}

			// Reset all handles
			menu.AddItem(new GUIContent("Reset All Handles"), false, () =>
			{
				foreach (Vector3Int handle in Lattice.GetHandles())
				{
					Undo.RecordObject(Lattice.GetHandle(handle), "Reset All Handles");
					Lattice.SetHandleOffset(handle, Vector3.zero);
				}
				_handleGizmos.Reset();
				EditorApplication.QueuePlayerLoopUpdate();
			});

			menu.AddSeparator("");

			// Copy selected index
			if (_selectedHandles.Count != 1)
			{
				menu.AddDisabledItem(new GUIContent("Copy Selected Index"));

			}
			else
			{
				Vector3Int handle = _selectedHandles.Handles[0];
				menu.AddItem(new GUIContent($"Copy Selected Index"), false, () =>
				{
					EditorGUIUtility.systemCopyBuffer = $"Vector3({handle.x},{handle.y},{handle.z})";
					Debug.Log($"Copied selected handle index: {handle}\n" +
						"Can be pasted into a Vector3 or Vector3Int field in the inspector.");
				});
			}

			// Copy selected indices
			if (_selectedHandles.Count == 0)
			{
				menu.AddDisabledItem(new GUIContent($"Copy Selected Indices"));
			}
			else
			{
				menu.AddItem(new GUIContent($"Copy Selected Indices"), false, () =>
				{
					int count = _selectedHandles.Count;

					string values = $"{{" +
						$"\"name\":\"size\"," +
						$"\"type\":12," +
						$"\"val\":{count}" +
					$"}},";

					string debug = $"Copied {count} selected handle indices.\n" +
						"Can be pasted into a Vector3 or Vector3Int list in the inspector. " +
						"The following were selected:\n";

					foreach (Vector3Int handle in _selectedHandles.Handles)
					{
						values += $"{{" +
							$"\"name\":\"data\"," +
							$"\"type\":21," +
							$"\"val\":\"Vector3({handle.x},{handle.y},{handle.z})\"" +
						$"}},";

						debug += $"{handle}\n";
					}

					values = values[..^1];
					debug = debug[..^1];

					string property = $"GenericPropertyJSON:{{" +
						$"\"name\":\"_indices\"," +
						$"\"type\":-1," +
						$"\"arraySize\":{count}," +
						$"\"arrayType\":\"Vector3Int\"," +
						$"\"children\":[" +
							$"{{" +
								$"\"name\":\"Array\"," +
								$"\"type\":-1," +
								$"\"arraySize\":{count}," +
								$"\"arrayType\":\"Vector3Int\"," +
								$"\"children\":[{values}]" +
							$"}}" +
						$"]" +
					$"}}";

					EditorGUIUtility.systemCopyBuffer = property;
					Debug.Log(debug);
				});
			}

			menu.AddSeparator("");

			// Fit lattice to renderer/transform
			FitToTransformMenuItem(menu);

			menu.AddSeparator("");

			// Lattice preferences window
			menu.AddItem(new GUIContent("Lattice Preferences..."), false, LatticeSettings.OpenPreferences);

			menu.ShowAsContext();
		}

		private void FitToTransformMenuItem(GenericMenu menu)
		{
			Transform transform = Lattice.transform;

			Vector3 originalPosition = transform.localPosition;
			Quaternion originalRotation = transform.localRotation;
			Vector3 originalScale = transform.localScale;

			bool changed = false;

			void ResetTransform()
			{
				transform.localPosition = originalPosition;
				transform.localRotation = originalRotation;
				transform.localScale = originalScale;
			}

			void OnSelection(Object obj, bool canceled)
			{
				Vector3 newPosition = transform.localPosition;
				Quaternion newRotation = transform.localRotation;
				Vector3 newScale = transform.localScale;

				ResetTransform();

				if (changed)
				{
					Undo.RecordObject(transform, "Fit Lattice");
					transform.localPosition = newPosition;
					transform.localRotation = newRotation;
					transform.localScale = newScale;
				}
			}

			// Fit to renderer
			menu.AddItem(new GUIContent("Fit To Renderer..."), false, () =>
			{
				void OnChange(Object obj)
				{
					if (obj is Renderer target)
					{
						Lattice.FitToTransform(target.transform, false);
						changed = true;
					}
					else
					{
						ResetTransform();
						changed = false;
					}
				}

				SearchService.ShowObjectPicker(OnSelection, OnChange, "h:", "", typeof(Renderer));
			});

			// Fit to transform
			menu.AddItem(new GUIContent("Fit To Transform..."), false, () =>
			{
				void OnChange(Object obj)
				{
					if (obj is Transform target)
					{
						Lattice.FitToTransform(target, true);
						changed = true;
					}
					else
					{
						ResetTransform();
						changed = false;
					}
				}

				SearchService.ShowObjectPicker(OnSelection, OnChange, "h: is:visible", "", typeof(Transform));
			});
		}

		#endregion
	}
}
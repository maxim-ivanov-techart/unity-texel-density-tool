using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

public class TexelDensityToolWindow : EditorWindow
{
	private enum ScopeMode { SceneObject, PrefabAsset, FolderBatch }
	
	private enum Status { Low, Ok, High, Error }

	private class MeshResult
	{
		public string meshName;
		public Status status;
		public float texelDensity;
		public float worldArea;
		public float uvArea;
	}

	private class FolderAssetEntry
	{
		public GameObject asset;
		public string path;
		public bool expanded;
		public Vector2 scroll;
		
		public readonly List<MeshResult> meshResults = new();

		public bool hasOverall;
		public float overallTd;
		public Status overallStatus;
		public int meshCount;
	}
	
	private class CalculationContext
	{
		public List<MeshResult> Results { get; }
		public Action<bool, float, Status, int> SetOverall { get; }
	
		public bool HasOverall { get; set; }
		public float OverallTd { get; set; }
		public Status OverallStatus { get; set; }
		public int MeshCount { get; set; }

		public CalculationContext(
			List<MeshResult> results,
			Action<bool, float, Status, int> setOverall)
		{
			Results = results;
			SetOverall = setOverall;
			HasOverall = false;
			OverallTd = 0f;
			OverallStatus = Status.Error;
			MeshCount = 0;
		}

		public void UpdateOverall(bool hasOverall, float td, Status status, int count)
		{
			HasOverall = hasOverall;
			OverallTd = td;
			OverallStatus = status;
			MeshCount = count;
			SetOverall?.Invoke(hasOverall, td, status, count);
		}
	}

	private readonly struct MeshItem
	{
		public readonly Mesh mesh;
		public readonly Transform transform;
		public readonly string displayName;

		public MeshItem(Mesh mesh, Transform transform, string displayName)
		{
			this.mesh = mesh;
			this.transform = transform;
			this.displayName = displayName;
		}
	}

	private ScopeMode _scopeMode = ScopeMode.SceneObject;
	private GameObject _targetObject;
	private Object _targetAsset;
	private DefaultAsset _targetFolder;
	
	private readonly List<string> _batchAssetPaths = new();
	private readonly List<MeshResult> _meshResults = new();
	private readonly List<FolderAssetEntry> _folderEntries = new();

	private int _textureResolution = 2048;
	private float _targetTexelDensity = 2048f;
	private float _tolerancePercent = 10f;

	private Vector2 _scroll;
	private Vector2 _folderScroll;

	private bool _hasOverall;
	private float _overallTd;
	private Status _overallStatus;
	private int _overallMeshCount;

	[MenuItem("Tools/Texel Density/Calculator")]
	public static void Open()
	{
		GetWindow<TexelDensityToolWindow>("Texel Density");
	}
	
	public void OnGUI()
	{
		EditorGUILayout.LabelField("Texel Density Calculator", EditorStyles.boldLabel);
		_scopeMode = (ScopeMode)GUILayout.Toolbar(
			(int)_scopeMode,
			new[] { "Scene Object", "Prefab/Model", "Folder (Batch)" }
		);
		EditorGUILayout.HelpBox($"Mode: {_scopeMode}", MessageType.Info);
		
		switch (_scopeMode)
		{
			case ScopeMode.SceneObject:
				_targetObject = (GameObject)EditorGUILayout.ObjectField(
					"Target Object (Scene)", _targetObject, typeof(GameObject), true);
				break;

			case ScopeMode.PrefabAsset:
				_targetAsset = EditorGUILayout.ObjectField(
					"Target Asset (Prefab/FBX)", _targetAsset, typeof(Object), false);
				break;

			case ScopeMode.FolderBatch:
				_targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
					"Target Folder", _targetFolder, typeof(DefaultAsset), false);
				break;
		}
		
		_textureResolution = EditorGUILayout.IntPopup(
			"Texture Resolution", _textureResolution,
			new[] { "512", "1024", "2048", "4096" },
			new[] { 512, 1024, 2048, 4096 });

		_targetTexelDensity = EditorGUILayout.FloatField(
			"Target Texel Density (px/m)", _targetTexelDensity);
		
		_tolerancePercent = EditorGUILayout.FloatField(
			"Tolerance (%)",
			_tolerancePercent
		);

		GUILayout.Space(10);

		bool canCalculate =
			(_scopeMode == ScopeMode.SceneObject && _targetObject != null) ||
			(_scopeMode == ScopeMode.PrefabAsset  && _targetAsset != null)  ||
			(_scopeMode == ScopeMode.FolderBatch  && _targetFolder != null);

		using (new EditorGUI.DisabledScope(!canCalculate))
		{
			if (GUILayout.Button("Calculate"))
			{
				switch (_scopeMode)
				{
					case ScopeMode.SceneObject:
						CalculateSceneObject();
						break;

					case ScopeMode.PrefabAsset:
						CalculatePrefabOrModelAsset();
						break;

					case ScopeMode.FolderBatch:
						ScanFolder();
						break;
				}
			}
		}

		if (_scopeMode == ScopeMode.FolderBatch)
		{
			DrawFolderUI();
			return;
		}

		GUILayout.Space(10);
		DrawMeshResultsList(_meshResults, ref _scroll, height: 220);

		GUILayout.Space(10);
		EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		if (_hasOverall)
		{
			GUILayout.Label($"Overall TD: {_overallTd:F2} px/m");
			GUILayout.Label($"Overall Status: {_overallStatus}");
			GUILayout.Label($"Meshes counted: {_overallMeshCount}");
		}
		else
		{
			GUILayout.Label("No overall result yet.");
		}

		EditorGUILayout.EndVertical();
	}

	private void CalculateSceneObject()
	{
		if (_targetObject == null)
		{
			return;
		}

		CalculateForRootIntoWindow(_targetObject);
	}
	
	private void CalculatePrefabOrModelAsset()
	{
		if (_targetAsset == null)
		{
			return;
		}
		
		string assetPath = AssetDatabase.GetAssetPath(_targetAsset);
		GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
		
		if (assetRoot == null)
		{
			return;
		}
		
		GameObject tempInstance = Instantiate(assetRoot);
		try
		{
			tempInstance.hideFlags = HideFlags.HideAndDontSave;
			ResetTransform(tempInstance.transform);
			CalculateForRootIntoWindow(tempInstance);
		}
		finally
		{
			DestroyImmediate(tempInstance);
		}
	}
	
	private void ScanFolder()
	{
		if (_targetFolder == null)
		{
			return;
		}
		
		_folderEntries.Clear();
		string folderPath = AssetDatabase.GetAssetPath(_targetFolder);
		
		_batchAssetPaths.Clear();
		
		try
		{
			EditorUtility.DisplayProgressBar(
				"Scanning Folder",
				"Searching for assets...",
				0f
			);

			AddGuidsToBatchList(AssetDatabase.FindAssets("t:Prefab", new[] { folderPath }));
			AddGuidsToBatchList(AssetDatabase.FindAssets("t:Model", new[] { folderPath }));

			if (_batchAssetPaths.Count == 0)
			{
				Debug.LogWarning($"No prefabs or models found in folder: {folderPath}");
				return;
			}
			
			for (int i = 0; i < _batchAssetPaths.Count; i++)
			{
				string path = _batchAssetPaths[i];
				
				EditorUtility.DisplayProgressBar(
					"Loading Assets",
					$"Loading {i + 1}/{_batchAssetPaths.Count}: {System.IO.Path.GetFileName(path)}",
					(float)i / _batchAssetPaths.Count * 0.3f 
				);

				GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);

				if (assetRoot == null)
				{
					Debug.LogWarning($"Failed to load asset at path: {path}");
					continue;
				}

				_folderEntries.Add(new FolderAssetEntry
				{
					asset = assetRoot,
					path = path,
					expanded = false,
					hasOverall = false,
					overallTd = 0f,
					overallStatus = Status.Error,
					meshCount = 0
				});
			}
			
			for (int i = 0; i < _folderEntries.Count; i++)
			{
				FolderAssetEntry entry = _folderEntries[i];
				
				float progress = 0.3f + ((float)i / _folderEntries.Count * 0.7f);
				
				EditorUtility.DisplayProgressBar(
					"Calculating Texel Density",
					$"Processing {i + 1}/{_folderEntries.Count}: {System.IO.Path.GetFileName(entry.path)}",
					progress
				);

				if (entry.asset == null)
				{
					continue;
				}

				GameObject tempInstance = null;
				try
				{
					tempInstance = Instantiate(entry.asset);
					if (tempInstance == null)
					{
						Debug.LogError($"Failed to instantiate asset: {entry.path}");
						continue;
					}

					tempInstance.hideFlags = HideFlags.HideAndDontSave;
					ResetTransform(tempInstance.transform);
					CalculateForRootIntoEntry(tempInstance, entry);
				}
				catch (Exception ex)
				{
					Debug.LogError($"Error processing asset {entry.path} : {ex.Message}");
					entry.hasOverall = false;
					entry.overallStatus = Status.Error;
				}
				finally
				{
					if (tempInstance != null)
					{
						DestroyImmediate(tempInstance);
					}
				}
			}

			EditorUtility.DisplayProgressBar(
				"Complete",
				"Texel density calculation finished!",
				1f
			);
		}
		catch (Exception ex)
		{
			Debug.LogError($"Critical error during folder scan: {ex.Message}\n{ex.StackTrace}");
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
	}

	private void CalculateForRootIntoWindow(GameObject root)
	{
		_meshResults.Clear();
	
		CalculationContext context = new CalculationContext(
			_meshResults,
			(has, td, status, count) =>
			{
				_hasOverall = has;
				_overallTd = td;
				_overallStatus = status;
				_overallMeshCount = count;
			}
		);
	
		CalculateForRoot(root, includeInactive: false, context);
	}

	private void CalculateForRootIntoEntry(GameObject root, FolderAssetEntry entry)
	{
		entry.meshResults.Clear();
	
		CalculationContext context = new CalculationContext(
			entry.meshResults,
			(has, td, status, count) =>
			{
				entry.hasOverall = has;
				entry.overallTd = td;
				entry.overallStatus = status;
				entry.meshCount = count;
			}
		);
	
		CalculateForRoot(root, includeInactive: true, context, forceNullReference: true);
	}

	private void CalculateForRoot(
		GameObject root,
		bool includeInactive,
		CalculationContext context,
		bool forceNullReference = false)
	{
		if (root == null)
		{
			context.UpdateOverall(false, 0f, Status.Error, 0);
			return;
		}

		_tolerancePercent = Mathf.Max(0f, _tolerancePercent);

		float totalWorldArea = 0f;
		float totalUvArea = 0f;
		int validMeshCount = 0;
		bool foundAny = false;

		foreach (MeshItem item in EnumerateMeshes(root, includeInactive, forceNullReference))
		{
			foundAny = true;

			MeshResult r = ComputeMeshResult(item, _tolerancePercent);
			context.Results.Add(r);

			if (r.status == Status.Error)
			{
				continue;
			}

			totalWorldArea += r.worldArea;
			totalUvArea += r.uvArea;
			validMeshCount++;
		}

		if (!foundAny)
		{
			Debug.LogWarning("No meshes found under target object (MeshFilter or SkinnedMeshRenderer).");
			context.UpdateOverall(false, 0f, Status.Error, 0);
			return;
		}

		if (validMeshCount == 0 || totalWorldArea <= 0f || totalUvArea <= 0f)
		{
			context.UpdateOverall(false, 0f, Status.Error, 0);
			return;
		}

		float overallTd = _textureResolution * Mathf.Sqrt(totalUvArea / totalWorldArea);
		Status overallStatus = GetTexelDensityStatus(overallTd, _targetTexelDensity, _tolerancePercent);

		context.UpdateOverall(true, overallTd, overallStatus, validMeshCount);
	}

	private IEnumerable<MeshItem> EnumerateMeshes(GameObject root, bool includeInactive, bool forceNullReference)
	{
		MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive);
		for (int i = 0; i < meshFilters.Length; i++)
		{
			MeshFilter mf = meshFilters[i];
			yield return new MeshItem(
				mf.sharedMesh,
				mf.transform,
				mf.gameObject.name
			);
		}

		SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
		for (int i = 0; i < skinned.Length; i++)
		{
			SkinnedMeshRenderer smr = skinned[i];
			yield return new MeshItem(
				smr.sharedMesh,
				smr.transform,
				smr.gameObject.name + " (Skinned)"
			);
		}
	}

	private MeshResult ComputeMeshResult(MeshItem item, float tolerancePercent)
	{
		if (item.mesh == null)
		{
			Debug.LogWarning($"Mesh is null for object: {item.displayName}");
			return new MeshResult
			{
				meshName = item.displayName,
				status = Status.Error,
				texelDensity = 0f,
				worldArea = 0f,
				uvArea = 0f,
			};
		}
		
		List<Vector2> uvCheck = new List<Vector2>();
		item.mesh.GetUVs(0, uvCheck);
		if (uvCheck.Count == 0)
		{
			Debug.LogWarning($"Mesh '{item.displayName}' has no UV coordinates. Texel density cannot be calculated.");
			return new MeshResult
			{
				meshName = item.displayName,
				status = Status.Error,
				texelDensity = 0f,
				worldArea = 0f,
				uvArea = 0f,
			};
		}

		float worldArea = CalculateWorldArea(item.mesh, item.transform.localToWorldMatrix);
		float uvArea = CalculateUvArea(item.mesh);

		if (worldArea <= 0f)
		{
			Debug.LogWarning($"Mesh '{item.displayName}' has zero or negative world area ({worldArea}).");
			return new MeshResult
			{
				meshName = item.displayName,
				status = Status.Error,
				texelDensity = 0f,
				worldArea = worldArea,
				uvArea = uvArea,
			};
		}

		if (uvArea <= 0f)
		{
			Debug.LogWarning($"Mesh '{item.displayName}' has zero or negative UV area ({uvArea}).");
			return new MeshResult
			{
				meshName = item.displayName,
				status = Status.Error,
				texelDensity = 0f,
				worldArea = worldArea,
				uvArea = uvArea,
			};
		}

		float texelDensity = _textureResolution * Mathf.Sqrt(uvArea / worldArea);
		Status status = GetTexelDensityStatus(texelDensity, _targetTexelDensity, tolerancePercent);

		return new MeshResult
		{
			meshName = item.displayName,
			status = status,
			texelDensity = texelDensity,
			worldArea = worldArea,
			uvArea = uvArea,
		};
	}

	private static float CalculateWorldArea(Mesh mesh, Matrix4x4 localToWorld)
	{
		if (mesh == null)
		{
			return 0f;
		}

		List<Vector3> vertices = new List<Vector3>();
		mesh.GetVertices(vertices);
		if (vertices.Count == 0)
		{
			return 0f;
		}

		double area = 0.0;

		for (int submeshIndex = 0; submeshIndex < mesh.subMeshCount; submeshIndex++)
		{
			List<int> triangles = new List<int>();
			mesh.GetTriangles(triangles, submeshIndex);

			for (int i = 0; i < triangles.Count; i += 3)
			{
				Vector3 triangleVertex0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
				Vector3 triangleVertex1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
				Vector3 triangleVertex2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);

				area += 0.5 * Vector3.Cross(triangleVertex1 - triangleVertex0,
					triangleVertex2 - triangleVertex0).magnitude;
			}
		}
		return (float)area;
	}

	private static float CalculateUvArea(Mesh mesh)
		{
			if (mesh == null)
			{
				return 0f;
			}
			List<Vector2> uv = new List<Vector2>();
			mesh.GetUVs(0, uv);
		
			if (uv.Count == 0)
			{
				Debug.LogWarning($"Mesh '{mesh.name}' has no UV coordinates in channel 0.");
				return 0f;
			}
		
			int vertexCount = mesh.vertexCount;
			if (uv.Count != vertexCount)
			{
				Debug.LogWarning($"Mesh '{mesh.name}': UV count ({uv.Count}) doesn't match vertex count ({vertexCount}).");
				return 0f;
			}
			double area = 0.0;
		
			for (int submeshIndex = 0; submeshIndex < mesh.subMeshCount; submeshIndex++)
			{
				List<int> triangles = new List<int>();
				mesh.GetTriangles(triangles, submeshIndex);

				for (int i = 0; i < triangles.Count; i += 3)
				{
					int idx0 = triangles[i];
					int idx1 = triangles[i + 1];
					int idx2 = triangles[i + 2];
				
					if (idx0 >= uv.Count || idx1 >= uv.Count || idx2 >= uv.Count)
					{
						Debug.LogWarning($"Mesh '{mesh.name}': Invalid triangle indices in submesh {submeshIndex}");
						continue;
					}

					Vector2 a = uv[idx1] - uv[idx0];
					Vector2 b = uv[idx2] - uv[idx0];

					double cross = (double)a.x * b.y - (double)a.y * b.x;
					area += 0.5 * Math.Abs(cross);
				}
			}
			return (float)area;
		}

		private Status GetTexelDensityStatus(float texelDensity, float targetTexelDensity, float tolerancePercent)
		{
			float differencePercent =
				(texelDensity - targetTexelDensity) / targetTexelDensity * 100f;

			if (differencePercent < -tolerancePercent)
			{
				return Status.Low;
			}

			return differencePercent > tolerancePercent ? Status.High : Status.Ok;
		}

		private void AddGuidsToBatchList(string[] guids)
		{
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			
				if (!_batchAssetPaths.Contains(path))
				{
					_batchAssetPaths.Add(path);
				}
			}
		}

		private void DrawFolderUI()
		{
			EditorGUILayout.LabelField("Folder Assets", EditorStyles.boldLabel);

			if (_folderEntries.Count == 0)
			{
				EditorGUILayout.LabelField("No assets scanned yet. Click Calculate to scan folder.");
				return;
			}

			_folderScroll = EditorGUILayout.BeginScrollView(_folderScroll, GUILayout.Height(350));

			for (int i = 0; i < _folderEntries.Count; i++)
			{
				FolderAssetEntry entry = _folderEntries[i];

				EditorGUILayout.BeginVertical(EditorStyles.helpBox);

				entry.expanded = EditorGUILayout.Foldout(entry.expanded, entry.path, true);
				if (!entry.expanded)
				{
					EditorGUILayout.EndVertical();
					continue;
				}

				DrawMeshResultsList(entry.meshResults, ref entry.scroll, height: 220);

				EditorGUILayout.Space(6);

				if (entry.hasOverall)
				{
					EditorGUILayout.LabelField($"Overall TD: {entry.overallTd:F2} px/m");
					EditorGUILayout.LabelField($"Status: {entry.overallStatus}");
					EditorGUILayout.LabelField($"Meshes counted: {entry.meshCount}");
				}
				else
				{
					EditorGUILayout.LabelField("No overall result.");
				}

				EditorGUILayout.EndVertical();
			}

			EditorGUILayout.EndScrollView();
		}
	
		private static void DrawMeshResultsList(
			List<MeshResult> results, 
			ref Vector2 scroll, 
			float height)
		{
			EditorGUILayout.LabelField("Meshes", EditorStyles.boldLabel);

			DrawMeshResultsHeader();

			if (results == null || results.Count == 0)
			{
				EditorGUILayout.LabelField("No mesh results yet.");
				return;
			}

			scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(height));
			DrawMeshResultsRows(results);
			EditorGUILayout.EndScrollView();
		}
	
		private static void DrawMeshResultsHeader()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
			GUILayout.Label("Status", GUILayout.Width(60f));
			GUILayout.Label("Mesh", GUILayout.ExpandWidth(true));
			GUILayout.Label("TD", GUILayout.Width(80f));
			EditorGUILayout.EndHorizontal();
		}
	
		private static void DrawMeshResultsRows(List<MeshResult> results)
		{
			for (int i = 0; i < results.Count; i++)
			{
				MeshResult r = results[i];
		
				EditorGUILayout.BeginHorizontal();
			
				Color originalColor = GUI.contentColor;
				GUI.contentColor = GetStatusColor(r.status);
				GUILayout.Label(r.status.ToString(), GUILayout.Width(60f));
				GUI.contentColor = originalColor;
			
				GUILayout.Label(r.meshName, GUILayout.ExpandWidth(true));
				GUILayout.Label(r.texelDensity.ToString("F2"), GUILayout.Width(80f));
		
				EditorGUILayout.EndHorizontal();
			}
		}
	
		private static Color GetStatusColor(Status status)
		{
			switch (status)
			{
				case Status.Ok:
					return new Color(0.3f, 0.8f, 0.3f);
				case Status.Low:
					return new Color(1f, 0.6f, 0f);
				case Status.High:
					return new Color(1f, 0.6f, 0f);
				case Status.Error:
					return new Color(1f, 0.3f, 0.3f);
				default:
					return Color.white;
			}
		}
	
		private static void ResetTransform(Transform t)
		{
			t.position = Vector3.zero;
			t.rotation = Quaternion.identity;
			t.localScale = Vector3.one;
		}
}
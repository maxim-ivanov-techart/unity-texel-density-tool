using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class TexelDensityToolWindow : EditorWindow
{
	private enum ScopeMode { SceneObject, PrefabAsset, FolderBatch }
	
	private ScopeMode _scopeMode = ScopeMode.SceneObject;
	private GameObject _targetObject;
	private Object _targetAsset;
	private DefaultAsset _targetFolder;
	private List<string> _batchAssetPaths = new ();
	
	private int _textureResolution = 2048;
	private float _targetTexelDensity = 2048f;
	private float _tolerancePercent = 10f;
	
	private Vector2 _scroll;
	private string _resultText = "";

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
		
		GUILayout.Space(10);
		
		if (string.IsNullOrEmpty(_resultText))
		{
			return;
		}
		
		EditorGUILayout.LabelField("Result",  EditorStyles.boldLabel);
		_scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(200));
		EditorGUILayout.TextArea(_resultText);
		EditorGUILayout.EndScrollView();
	}

	private void CalculateSceneObject()
	{
		if (_targetObject == null)
		{
			return;
		}
		
		_resultText = "";
		CalculateForRoot(_targetObject);
	}
	
	private void CalculatePrefabOrModelAsset()
	{
		_resultText = "";
		if (_targetAsset == null)
		{
			return;
		}
		
		string assetPath = AssetDatabase.GetAssetPath(_targetAsset);
		GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
		
		if (assetRoot == null)
		{
			_resultText = "Selected asset is not a GameObject (Prefab/Model).";
			return;
		}
		
		GameObject tempInstance = Instantiate(assetRoot);
		
		tempInstance.hideFlags = HideFlags.HideAndDontSave;
		tempInstance.transform.position = Vector3.zero;
		tempInstance.transform.rotation = Quaternion.identity;
		tempInstance.transform.localScale = Vector3.one;
		
		CalculateForRoot(tempInstance);
		DestroyImmediate(tempInstance);
	}
	
	private void ScanFolder()
	{
		if (_targetFolder == null)
		{
			_resultText = "Please select a folder first.";
			return;
		}
		
		string folderPath = AssetDatabase.GetAssetPath(_targetFolder);
		
		_batchAssetPaths.Clear();
		_resultText = "";
		
		string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
		string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
		
		AddGuidsToBatchList(prefabGuids);
		AddGuidsToBatchList(modelGuids);
		
		if (_batchAssetPaths.Count == 0)
		{
			_resultText = $"No Prefabs or Models found in folder:\n{folderPath}";
			return;
		}
		
		_resultText = "";

		for (int i = 0; i < _batchAssetPaths.Count; i++)
		{
			string path = _batchAssetPaths[i];
			GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);

			if (assetRoot == null)
			{
				_resultText += $"{i + 1}. {path}\nERROR: not a GameObject\n\n";
				continue;
			}

			GameObject tempInstance = Instantiate(assetRoot);
			tempInstance.hideFlags = HideFlags.HideAndDontSave;

			tempInstance.transform.position = Vector3.zero;
			tempInstance.transform.rotation = Quaternion.identity;
			tempInstance.transform.localScale = Vector3.one;
			
			_resultText += $"==============================\n";
			_resultText += $"{i + 1}. {path}\n";
			_resultText += $"==============================\n";
			
			CalculateForRoot(tempInstance);

			_resultText += "\n\n";

			DestroyImmediate(tempInstance);
		}
	}

	private static float CalculateWorldArea(Mesh mesh, Matrix4x4 localToWorld)
	{
		Vector3[] vertices = mesh.vertices;
		int[] triangles = mesh.triangles;
		double area = 0.0;
		
		for (int i = 0; i < triangles.Length; i += 3)
		{
			Vector3 triangleVertex0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i]]);
			Vector3 triangleVertex1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]);
			Vector3 triangleVertex2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
			
			area += 0.5 * Vector3.Cross(triangleVertex1 - triangleVertex0,
				triangleVertex2 - triangleVertex0).magnitude;
		}
		return (float)area;
	}
	
	private static float CalculateUvArea(Mesh mesh)
	{
		Vector2[] uv = mesh.uv;
		if (uv == null || uv.Length == 0)
		{
			return 0f;
		}
		
		int[] triangles = mesh.triangles;
		double area = 0.0;
		
		for (int i = 0; i < triangles.Length; i += 3)
		{
			Vector2 a = uv[triangles[i + 1]] - uv[triangles[i]];
			Vector2 b = uv[triangles[i + 2]] - uv[triangles[i]];

			double cross = (double)a.x * b.y - (double)a.y * b.x;
			area += 0.5 * System.Math.Abs(cross);
		}
		return (float)area;
	}

	private string GetTexelDensityStatus(float texelDensity, float targetTexelDensity, float tolerancePercent)
	{
		float differencePercent =
			(texelDensity - targetTexelDensity) / targetTexelDensity * 100f;

		if (differencePercent < -tolerancePercent)
		{
			return "LOW";
		}
		
		return differencePercent > tolerancePercent ? "HIGH" : "OK";
	}

	private void ProcessMesh(
		Mesh mesh,
		Transform transform,
		string displayName,
		ref float totalWorldArea,
		ref float totalUvArea,
		ref int validMeshCount
	)
	{
		if (mesh == null)
		{
			return;
		}
		
		float worldArea = CalculateWorldArea(mesh, transform.localToWorldMatrix);
		float uvArea = CalculateUvArea(mesh);
		
		if (worldArea <= 0f || uvArea <= 0f)
		{
			_resultText += $"{displayName} | ERROR: World={worldArea:F3}, UV={uvArea:F3}\n";
			return;
		}
		float texelDensity = _textureResolution * Mathf.Sqrt(uvArea / worldArea);
		string status = GetTexelDensityStatus(texelDensity, _targetTexelDensity, _tolerancePercent);
		
		_resultText +=
			$"{displayName} | " +
			$"TD = {texelDensity:F2} px/m | " +
			$"Status = {status}\n";
		
		totalWorldArea += worldArea;
		totalUvArea += uvArea;
		validMeshCount++;
	}
	
	private void CalculateForRoot(GameObject root)
	{
		float totalWorldArea = 0f;
		float totalUvArea = 0f;
		int validMeshCount = 0;
		
		MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();
		SkinnedMeshRenderer[] skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
		
		if (meshFilters.Length == 0 && skinnedMeshRenderers.Length == 0)
		{
			Debug.LogWarning("No MeshFilters found under target object.");
			return;
		}
		
		_tolerancePercent = Mathf.Max(0f, _tolerancePercent);
		
		foreach (MeshFilter meshFilter in meshFilters)
		{
			ProcessMesh(
				meshFilter.sharedMesh,
				meshFilter.transform,
				meshFilter.gameObject.name,
				ref totalWorldArea,
				ref totalUvArea,
				ref validMeshCount
			);
		}

		foreach (SkinnedMeshRenderer skinnedRenderer in skinnedMeshRenderers)
		{
			ProcessMesh(
				skinnedRenderer.sharedMesh,
				skinnedRenderer.transform,
				skinnedRenderer.gameObject.name + " (Skinned)",
				ref totalWorldArea,
				ref totalUvArea,
				ref validMeshCount
			);
		}
		
		_resultText += "\n--- Overall ---\n";
		if (validMeshCount == 0 || totalWorldArea <= 0f || totalUvArea <= 0f)
		{
			_resultText += "Overall TD: N/A (no valid meshes)\n";
		}
		else
		{
			float overallTd = _textureResolution * Mathf.Sqrt(totalUvArea / totalWorldArea);
			string overallStatus = GetTexelDensityStatus(
				overallTd,
				_targetTexelDensity,
				_tolerancePercent
			);
			
			_resultText +=
				$"Meshes counted: {validMeshCount}\n" + 
				$"Total World Area: {totalWorldArea:F3} m²\n" + 
				$"Total UV Area: {totalUvArea:F3}\n" + 
				$"Overall TD: {overallTd:F2} px/m\n" + 
				$"Target TD: {_targetTexelDensity:F0} px/m\n" + 
				$"Status: {overallStatus}\n";
		}
	}
	
	private void AddGuidsToBatchList(string[] guids)
	{
		foreach (string guid in guids)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);

			if (!_batchAssetPaths.Contains(path))
			{
				_batchAssetPaths.Add(path);
			}
		}
	}
}

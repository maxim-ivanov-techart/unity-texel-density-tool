# Texel Density Calculator (Unity Editor Tool)

A lightweight Unity Editor Window for analyzing **Texel Density (px/m)** across meshes in scenes, prefabs, models, and entire folders.  
Designed for **technical artists** and **environment artists** who want consistent texture scale and clean UVs.

### Formula

***Texel Density = TextureResolution × √(UV_Area / World_Area)***

## Features

- Per-mesh Texel Density calculation  
- Target TD comparison with tolerance  
- Overall Texel Density per object / asset  
- Expandable asset list in Folder mode  

## Supports
- MeshFilter  
- SkinnedMeshRenderer  
- All submeshes are included  
- World area is calculated in world space  
- UV area uses UV channel 0 

## Supported scopes
- Scene Objects
- Prefab / FBX assets
- Folder batch processing

---

# Installation
 1. Copy TexelDensityToolWindow.cs into Editor folder. For example Assets/Scripts/Editor/
 2. Open the Tool. Tools → Texel Density → Calculator

## Settings
- Texture Resolution - Texture size used for TD calculation (512–4096)
- Target Texel Density - Desired TD in px/m
- Tolerance (%) - Allowed deviation from target

---

# Usage
## 1. Scene Object
- Assign a scene GameObject
- Click Calculate

Includes child meshes. Ignores inactive objects
<img width="724" height="574" alt="image" src="https://github.com/user-attachments/assets/96666d64-35d8-4561-a8e8-7ab027f4873c" />

## 2. Prefab / Model
- Assign a Prefab or FBX
- Click Calculate

Asset is instantiated temporarily. Transform is reset before calculation
<img width="724" height="460" alt="image" src="https://github.com/user-attachments/assets/8d9c5942-b42c-4957-ae35-9d9a0db426e1" />

## 3. Folder (Batch)
- Assign a Project folder
- Click Calculate

Scans Prefab + FBX assets. Each asset has: per-mesh results, overall TD. Expandable UI.
<img width="721" height="636" alt="image" src="https://github.com/user-attachments/assets/2c4ef79d-b546-4406-a2c8-0328eeaa2a60" />

---

# Error Cases

> [!CAUTION]
> A mesh is marked as Error if:
> - Mesh is missing
> - No UVs in channel 0
> - Zero or invalid world area
> - Zero or invalid UV area




